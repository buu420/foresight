using System.Buffers.Binary;
using System.Collections.ObjectModel;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

public sealed record TitleMenuItem(int RawKey, string Label, int Position, int Count);

public sealed class TitleMenuSnapshot
{
    private readonly IReadOnlyDictionary<int, TitleMenuItem> byRawKey;

    internal TitleMenuSnapshot(IEnumerable<(int RawKey, string Label)> captured)
    {
        var ordered = captured.OrderBy(item => item.RawKey).ToArray();
        Items = new ReadOnlyCollection<TitleMenuItem>(ordered
            .Select((item, index) => new TitleMenuItem(item.RawKey, item.Label, index + 1, ordered.Length))
            .ToArray());
        RawKeys = new ReadOnlyCollection<int>(Items.Select(item => item.RawKey).ToArray());
        byRawKey = new ReadOnlyDictionary<int, TitleMenuItem>(Items.ToDictionary(item => item.RawKey));
    }

    public IReadOnlyList<TitleMenuItem> Items { get; }
    public IReadOnlyList<int> RawKeys { get; }

    public bool TryResolve(int rawKey, out TitleMenuItem item) =>
        byRawKey.TryGetValue(rawKey, out item!);
}

public interface ITitleCapture
{
    bool IsBuilderActive { get; }
    IDisposable BeginBuilder();
    void ObserveLocalizedResult(int fileId, int messageId, nuint resultAddress, string label);
    void ObserveConstructedRow(nuint recordAddress, nuint rowControl, string label);
    void ReportBuilderError(string error);
    bool TryCompleteBuilder(out TitleMenuSnapshot snapshot, out string error);
}

public sealed class TitleCapture : ITitleCapture
{
    public const int RowStride = 0x1C;
    public const int MaximumRawKey = 6;

    private static readonly IReadOnlyDictionary<(int FileId, int MessageId), int> KnownSourceKeys =
        new ReadOnlyDictionary<(int, int), int>(new Dictionary<(int, int), int>
        {
            [(0x41, 3)] = 0,
            [(0x41, 0)] = 1,
            [(0x41, 1)] = 2,
            [(0x41, 2)] = 3,
            [(0x41, 6)] = 4,
            [(0x23, 0x24)] = 5,
            [(0x3A, 4)] = 6,
        });

    private readonly ThreadLocal<BuilderState?> current = new();

    public bool IsBuilderActive => current.Value is not null;

    public IDisposable BeginBuilder()
    {
        if (current.Value is not null)
        {
            throw new InvalidOperationException("A title builder capture is already active on this thread.");
        }

        var state = new BuilderState();
        current.Value = state;
        return new BuilderScope(this, state);
    }

    public void ObserveLocalizedResult(
        int fileId,
        int messageId,
        nuint resultAddress,
        string label)
    {
        var state = current.Value;
        if (state is null || resultAddress == 0 || string.IsNullOrWhiteSpace(label) ||
            !KnownSourceKeys.TryGetValue((fileId, messageId), out var rawKey))
        {
            return;
        }

        var displacement = checked((nuint)(rawKey * RowStride));
        if (resultAddress < displacement)
        {
            state.Errors.Add($"Localized row {rawKey} result address underflows its row displacement.");
            return;
        }

        state.BaseCandidates[rawKey] = resultAddress - displacement;
    }

    public void ObserveConstructedRow(nuint recordAddress, nuint rowControl, string label)
    {
        var state = current.Value;
        if (state is null || rowControl == 0)
        {
            return;
        }

        if (recordAddress == 0 || string.IsNullOrWhiteSpace(label))
        {
            state.Errors.Add("A constructed title row had no readable record address or label.");
            return;
        }

        state.Rows.Add(new CapturedRow(recordAddress, new string(label.AsSpan())));
    }

    public void ReportBuilderError(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        current.Value?.Errors.Add(error);
    }

    public bool TryCompleteBuilder(out TitleMenuSnapshot snapshot, out string error)
    {
        snapshot = null!;
        var state = current.Value;
        if (state is null)
        {
            error = "No title builder capture scope is active.";
            return false;
        }

        if (state.Errors.Count > 0)
        {
            error = string.Join(" ", state.Errors);
            return false;
        }

        if (state.BaseCandidates.Count < 2)
        {
            error = "At least two localized row candidates are required to validate the title row-array base.";
            return false;
        }

        var distinctBases = state.BaseCandidates.Values.Distinct().ToArray();
        if (distinctBases.Length != 1)
        {
            error = "Localized title rows disagree on the row-array base.";
            return false;
        }

        var rowBase = distinctBases[0];
        var rows = new Dictionary<int, string>();
        foreach (var captured in state.Rows)
        {
            if (captured.RecordAddress < rowBase)
            {
                error = "A title row record precedes the validated row-array base.";
                return false;
            }

            var displacement = captured.RecordAddress - rowBase;
            if (displacement % RowStride != 0)
            {
                error = "A title row record is not aligned to the 0x1C-byte row stride.";
                return false;
            }

            var rawKeyValue = displacement / RowStride;
            if (rawKeyValue > MaximumRawKey)
            {
                error = $"A title row raw key {rawKeyValue} is outside 0..{MaximumRawKey}.";
                return false;
            }

            var rawKey = checked((int)rawKeyValue);
            if (!rows.TryAdd(rawKey, captured.Label))
            {
                error = $"Title raw key {rawKey} was constructed more than once.";
                return false;
            }
        }

        if (rows.Count == 0)
        {
            error = "The title builder produced no enabled rows.";
            return false;
        }

        snapshot = new TitleMenuSnapshot(rows.Select(pair => (pair.Key, pair.Value)));
        error = string.Empty;
        return true;
    }

    private void EndBuilder(BuilderState expected)
    {
        if (ReferenceEquals(current.Value, expected))
        {
            current.Value = null;
        }
    }

    private sealed class BuilderState
    {
        public Dictionary<int, nuint> BaseCandidates { get; } = [];
        public List<CapturedRow> Rows { get; } = [];
        public List<string> Errors { get; } = [];
    }

    private sealed record CapturedRow(nuint RecordAddress, string Label);

    private sealed class BuilderScope(TitleCapture owner, BuilderState state) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                owner.EndBuilder(state);
            }
        }
    }
}

public enum TitleModeKind
{
    Unknown,
    TapToStart,
    TitleMenu,
}

public sealed record TitleMenuNativeState(nuint Owner, nuint Manager, int RawFocusKey);

public sealed class TitleSceneInspector
{
    public const uint CurrentModeOffset = 0x290;
    public const uint OwnerOffset = 0x04;
    public const uint ManagerOffset = 0x10;
    public const uint ManagerFocusOffset = 0x2C4;
    public const uint TapToStartVtableRva = 0x3B7FC4;
    public const uint TitleMenuModeVtableRva = 0x3B7FA8;
    public const uint TitleSceneVtableRva = 0x3B806C;
    public const uint ManagerVtableRva = 0x3A5D0C;

    private readonly IReadableMemory memory;
    private readonly nuint imageBase;

    public TitleSceneInspector(IReadableMemory memory, nuint imageBase)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        this.imageBase = imageBase != 0 ? imageBase : throw new ArgumentOutOfRangeException(nameof(imageBase));
    }

    public TitleModeKind InspectMode(nuint scene)
    {
        if (!TryReadPointer(scene, out var sceneVtable) ||
            sceneVtable != imageBase + TitleSceneVtableRva ||
            !TryReadPointer(scene + CurrentModeOffset, out var mode) || mode == 0 ||
            !TryReadPointer(mode, out var vtable))
        {
            return TitleModeKind.Unknown;
        }

        if (vtable == imageBase + TapToStartVtableRva)
        {
            return TitleModeKind.TapToStart;
        }

        return vtable == imageBase + TitleMenuModeVtableRva
            ? TitleModeKind.TitleMenu
            : TitleModeKind.Unknown;
    }

    public bool TryInspectTitleMenu(
        nuint mode,
        out TitleMenuNativeState state,
        out string error)
    {
        state = null!;
        if (!TryReadPointer(mode, out var modeVtable) ||
            modeVtable != imageBase + TitleMenuModeVtableRva)
        {
            error = "Title menu mode vtable is invalid.";
            return false;
        }

        if (!TryReadPointer(mode + OwnerOffset, out var owner) || owner == 0 ||
            !TryReadPointer(owner, out var ownerVtable) ||
            ownerVtable != imageBase + TitleSceneVtableRva ||
            !TryReadPointer(owner + CurrentModeOffset, out var ownerMode) ||
            ownerMode != mode)
        {
            error = "Title menu owner chain is invalid.";
            return false;
        }

        if (!TryReadPointer(mode + ManagerOffset, out var manager) || manager == 0 ||
            !TryReadPointer(manager, out var managerVtable) ||
            managerVtable != imageBase + ManagerVtableRva)
        {
            error = "Title menu manager chain is invalid.";
            return false;
        }

        Span<byte> focusBytes = stackalloc byte[4];
        if (!memory.TryRead(manager + ManagerFocusOffset, focusBytes))
        {
            error = "Title menu manager focus key is unreadable.";
            return false;
        }

        state = new TitleMenuNativeState(owner, manager, BinaryPrimitives.ReadInt32LittleEndian(focusBytes));
        error = string.Empty;
        return true;
    }

    private bool TryReadPointer(nuint address, out nuint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        if (!memory.TryRead(address, bytes))
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        return true;
    }
}

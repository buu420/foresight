using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

public sealed class TopMenuCaptureScope : IDisposable
{
    public const uint ClassicRootVtableRva = 0x3A4024;
    public const uint TouchRootVtableRva = 0x3AA058;
    public const uint StatusBarVtableRva = 0x3ABCB8;
    public const uint StatusRendererReturnRva = 0x22F3BD;

    private const uint GlobalPointerRva = 0x41B4C4;
    private const uint ClassicManagerStackOffset = 0x2C0;
    private const uint ClassicStatusBarOffset = 0x2CC;
    private const uint ManagerFocusedKeyOffset = 0x2C4;
    private const uint GlobalRosterOwnerOffset = 0x28;
    private const uint GlobalReserveGateOffset = 0x10F84;
    private const uint GlobalReserveThresholdOffset = 0x110B0;
    private const uint RosterSlotsOffset = 0x219C;
    private const uint ReserveNamesOffset = 0x1908;
    private const int RosterSlotCount = 9;
    private const int RowCount = 7;
    private const int MaximumManagerStackCount = 16;
    private const int MaximumReserveNameBytes = 4_096;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly ThreadLocal<TopMenuCaptureScope?> Current = new();

    private readonly IReadableMemory memory;
    private readonly uint imageBase;
    private readonly uint root;
    private readonly uint expectedRootVtable;
    private readonly TopMenuStyle style;
    private readonly int owningThreadId = Environment.CurrentManagedThreadId;
    private readonly List<RenderObservation> renderObservations = [];
    private readonly List<ConstructedControl> controls = [];
    private readonly Dictionary<nuint, ConstructedControl> controlByPointer = [];
    private readonly Dictionary<nuint, Binding> bindingByControl = [];
    private readonly HashSet<int> boundKeys = [];
    private readonly HashSet<int> positions = [];
    private readonly List<string> errors = [];
    private nuint? bindingManager;
    private StatusCaptureState? statusState;
    private bool disposed;
    private bool published;
    private int abandoned;

    private TopMenuCaptureScope(
        IReadableMemory memory,
        uint imageBase,
        uint root,
        uint expectedRootVtable,
        TopMenuStyle style)
    {
        this.memory = memory;
        this.imageBase = imageBase;
        this.root = root;
        this.expectedRootVtable = expectedRootVtable;
        this.style = style;
        Current.Value = this;
    }

    public static bool TryBegin(
        IReadableMemory? memory,
        nuint imageBase,
        nuint root,
        TopMenuStyle style,
        out TopMenuCaptureScope scope,
        out string diagnostic)
    {
        scope = null!;
        try
        {
            if (Current.Value is { } existing)
            {
                if (!existing.IsAbandoned)
                {
                    diagnostic = "A top-menu capture scope is already active on this thread.";
                    return false;
                }
                Current.Value = null;
            }
            if (memory is null || !Enum.IsDefined(style) ||
                !FitsX86Address(imageBase) || !FitsAlignedX86Range(root, sizeof(uint)))
            {
                diagnostic = "Top-menu memory, style, image base, or root is unavailable or outside aligned x86 memory.";
                return false;
            }

            var rootVtableRva = style == TopMenuStyle.Classic
                ? ClassicRootVtableRva
                : TouchRootVtableRva;
            if (!TryAddX86(imageBase, rootVtableRva, out var expectedVtable))
            {
                diagnostic = "Top-menu root vtable resolution crosses the x86 address space.";
                return false;
            }
            if (!TryReadUInt32(memory, root, out var observedVtable) || observedVtable != expectedVtable)
            {
                diagnostic = "Top-menu root vtable is unreadable or does not match the selected classic/touch style.";
                return false;
            }

            scope = new TopMenuCaptureScope(
                memory,
                checked((uint)imageBase),
                checked((uint)root),
                expectedVtable,
                style);
            diagnostic = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            scope = null!;
            diagnostic = $"Top-menu scope creation failed safely: {exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    public bool TryRecordUtf8(nuint absoluteReturnAddress, string? text, out string diagnostic)
    {
        if (!TryUse(out diagnostic))
        {
            return false;
        }
        if (!TryNormalizeReturnAddress(absoluteReturnAddress, out var returnRva) ||
            !IsAllowedUtf8ReturnRva(returnRva))
        {
            return RecordFailure(
                "A top-menu UTF-8 observation has an unknown, unreachable, or style-mismatched return address.",
                out diagnostic);
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            return RecordFailure("A top-menu UTF-8 observation is blank.", out diagnostic);
        }

        renderObservations.Add(RenderObservation.Utf8(returnRva, new string(text.AsSpan())));
        diagnostic = string.Empty;
        return true;
    }

    public bool TryRecordConstructedControl(
        nuint control,
        int position,
        bool enabled,
        bool visible,
        out string diagnostic)
    {
        if (!TryUse(out diagnostic))
        {
            return false;
        }
        if (!FitsAlignedX86Range(control, sizeof(uint)) || position is < 0 or >= RowCount || !visible)
        {
            return RecordFailure(
                "A top-menu row control has an invalid pointer/position or is not visible.",
                out diagnostic);
        }
        if (controlByPointer.ContainsKey(control) || !positions.Add(position))
        {
            return RecordFailure("A top-menu row control or native position was observed more than once.", out diagnostic);
        }
        if (controls.Count >= RowCount)
        {
            return RecordFailure("More than seven top-menu row controls were observed.", out diagnostic);
        }

        var captured = new ConstructedControl(control, position, enabled, visible);
        controls.Add(captured);
        controlByPointer.Add(control, captured);
        diagnostic = string.Empty;
        return true;
    }

    public bool TryRecordManagerKeyBinding(
        nuint manager,
        nuint control,
        int key,
        out string diagnostic)
    {
        if (!TryUse(out diagnostic))
        {
            return false;
        }
        if (!FitsAlignedX86Range(manager, sizeof(uint)) ||
            !FitsAlignedX86Range(control, sizeof(uint)) || key < 0)
        {
            return RecordFailure(
                "A top-menu manager binding has an invalid manager, control, or native key.",
                out diagnostic);
        }
        if (bindingByControl.ContainsKey(control))
        {
            return RecordFailure("A top-menu control was bound more than once.", out diagnostic);
        }
        if (bindingManager is not null && bindingManager != manager)
        {
            return RecordFailure("More than one manager was observed for the top-menu row bindings.", out diagnostic);
        }
        if (!boundKeys.Add(key))
        {
            return RecordFailure("A duplicate native key was observed for the top-menu row bindings.", out diagnostic);
        }
        if (bindingByControl.Count >= RowCount)
        {
            return RecordFailure("More than seven top-menu manager bindings were observed.", out diagnostic);
        }

        bindingManager ??= manager;
        bindingByControl.Add(control, new Binding(manager, key));
        diagnostic = string.Empty;
        return true;
    }

    public bool TryBeginStatusBar(
        nuint statusBar,
        out TopMenuStatusCaptureScope statusScope,
        out string diagnostic)
    {
        statusScope = null!;
        if (!TryUse(out diagnostic))
        {
            return false;
        }
        if (statusState is not null)
        {
            return RecordFailure("The top-menu builder observed more than one StatusBar formatting scope.", out diagnostic);
        }

        try
        {
            if (!FitsAlignedX86Range(statusBar, sizeof(uint)) ||
                !TryAddX86(imageBase, StatusBarVtableRva, out var expectedStatusVtable))
            {
                return RecordFailure("The top-menu StatusBar pointer or vtable crosses x86 memory.", out diagnostic);
            }
            if (!TryReadUInt32(memory, statusBar, out var observedStatusVtable) ||
                observedStatusVtable != expectedStatusVtable)
            {
                return RecordFailure("The StatusBar vtable is unreadable or does not match the audited type.", out diagnostic);
            }
            if (style == TopMenuStyle.Classic &&
                (!TryReadRootPointer(ClassicStatusBarOffset, out var ownedStatusBar) || ownedStatusBar != statusBar))
            {
                return RecordFailure("The classic top-menu root does not own the observed StatusBar pointer.", out diagnostic);
            }

            statusState = new StatusCaptureState(checked((uint)statusBar), expectedStatusVtable);
            renderObservations.Add(RenderObservation.StatusBegin(statusState));
            statusScope = new TopMenuStatusCaptureScope(this, statusState, owningThreadId);
            diagnostic = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            return RecordFailure(
                $"StatusBar scope creation failed safely: {exception.GetType().Name}: {exception.Message}",
                out diagnostic);
        }
    }

    public bool TryCreateSnapshot(out TopMenuSnapshot snapshot, out string diagnostic)
    {
        snapshot = null!;
        if (!TryUse(out diagnostic))
        {
            return false;
        }
        if (published)
        {
            diagnostic = "This top-menu capture scope has already published its one immutable snapshot.";
            return false;
        }

        try
        {
            if (errors.Count > 0)
            {
                diagnostic = string.Join(" ", errors);
                return false;
            }
            if (statusState is null || !statusState.Completed || !statusState.Disposed || statusState.IsAbandoned)
            {
                diagnostic = "The top-menu requires one normally completed and disposed owned StatusBar formatting scope.";
                return false;
            }
            if (!TryParseRenderObservations(
                    out var parsedMembers,
                    out var captions,
                    out var time,
                    out var currency,
                    out var footerLines,
                    out diagnostic))
            {
                return false;
            }
            if (!TryCaptureRoster(parsedMembers, out var rosterCapture, out diagnostic))
            {
                return false;
            }
            if (!TryCorrelateControls(captions, out var capturedControls, out var managerCapture, out diagnostic))
            {
                return false;
            }

            var members = new List<TopMenuMemberSnapshot>(parsedMembers.Count);
            var reserveIndex = 0;
            foreach (var parsed in parsedMembers)
            {
                var name = parsed.Kind == TopMenuMemberKind.Active
                    ? parsed.Name!
                    : rosterCapture.ReserveNames[reserveIndex++].Value;
                members.Add(new TopMenuMemberSnapshot(name, parsed.Kind, parsed.Rows));
            }

            if (!TryRevalidateManager(managerCapture, out diagnostic) ||
                !TryRevalidateRoster(rosterCapture, out diagnostic) ||
                !TryRevalidateStatusBar(statusState, out diagnostic))
            {
                return false;
            }
            if (!TryReadUInt32(memory, root, out var finalRootVtable) || finalRootVtable != expectedRootVtable)
            {
                diagnostic = "The top-menu root vtable changed while dependent state was being captured.";
                return false;
            }

            var conditionalLines = statusState.Lines.Concat(footerLines).ToArray();
            var flattened = FlattenStatus(members, time, currency, conditionalLines);
            snapshot = new TopMenuSnapshot(
                style,
                capturedControls,
                managerCapture.FocusedKey,
                time,
                currency,
                members,
                conditionalLines,
                flattened);
            published = true;
            diagnostic = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            snapshot = null!;
            diagnostic = $"Top-menu memory capture failed safely: {exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId != owningThreadId)
        {
            Interlocked.Exchange(ref abandoned, 1);
            throw new InvalidOperationException("Top-menu capture scope disposal was attempted from a different thread.");
        }
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (ReferenceEquals(Current.Value, this))
        {
            Current.Value = null;
        }
    }

    internal bool TryRecordStatusLine(
        StatusCaptureState state,
        nuint absoluteReturnAddress,
        nuint wideStringAddress,
        out string diagnostic)
    {
        if (!TryUse(out diagnostic))
        {
            return false;
        }
        if (!ReferenceEquals(statusState, state) || state.Completed || state.Disposed || state.IsAbandoned)
        {
            return RecordFailure("A conditional line was observed outside the active owned StatusBar scope.", out diagnostic);
        }
        if (!TryNormalizeReturnAddress(absoluteReturnAddress, out var returnRva) ||
            returnRva != StatusRendererReturnRva)
        {
            return RecordFailure("A conditional line came from an unaudited StatusBar renderer caller.", out diagnostic);
        }
        if (state.Lines.Count >= 64)
        {
            return RecordFailure("The StatusBar rendered more than the defensive 64-line limit.", out diagnostic);
        }
        if (!new MsvcWideStringReader(memory).TryRead(wideStringAddress, out var line, out var readError))
        {
            return RecordFailure($"A conditional StatusBar line is invalid: {readError}", out diagnostic);
        }
        if (string.IsNullOrWhiteSpace(line))
        {
            return RecordFailure("A conditional StatusBar line is blank.", out diagnostic);
        }

        var aggregate = checked(state.AggregateCodeUnits + line.Length);
        if (aggregate > 4_096)
        {
            return RecordFailure("Conditional StatusBar text exceeds 4,096 aggregate UTF-16 code units.", out diagnostic);
        }
        state.AggregateCodeUnits = aggregate;
        state.Lines.Add(new string(line.AsSpan()));
        diagnostic = string.Empty;
        return true;
    }

    internal bool TryCompleteStatus(StatusCaptureState state, out string diagnostic)
    {
        if (!TryUse(out diagnostic))
        {
            return false;
        }
        if (!ReferenceEquals(statusState, state) || state.Disposed || state.IsAbandoned || state.Completed)
        {
            return RecordFailure("The StatusBar formatting scope cannot be completed from its current state.", out diagnostic);
        }
        state.Completed = true;
        renderObservations.Add(RenderObservation.StatusComplete(state));
        diagnostic = string.Empty;
        return true;
    }

    internal void DisposeStatus(StatusCaptureState state, int statusOwnerThreadId)
    {
        if (Environment.CurrentManagedThreadId != statusOwnerThreadId)
        {
            Interlocked.Exchange(ref state.Abandoned, 1);
            throw new InvalidOperationException("StatusBar capture scope disposal was attempted from a different thread.");
        }
        state.Disposed = true;
    }

    private bool TryParseRenderObservations(
        out List<ParsedMember> members,
        out List<string> captions,
        out string time,
        out string currency,
        out IReadOnlyList<string> footerLines,
        out string diagnostic)
    {
        members = [];
        captions = [];
        time = string.Empty;
        currency = string.Empty;
        var renderedFooter = new List<string>();
        footerLines = renderedFooter;
        var cursor = 0;
        var activeCount = 0;
        var reserveCount = 0;

        if (style == TopMenuStyle.Classic)
        {
            while (TryPeekUtf8(cursor, 0x23B1D0))
            {
                if (activeCount >= 3)
                {
                    diagnostic = "The classic top menu rendered more than three active members.";
                    return false;
                }
                var name = renderObservations[cursor++].Text!;
                if (!TryParseClassicRows(ref cursor, out var rows, out diagnostic))
                {
                    return false;
                }
                members.Add(new ParsedMember(name, TopMenuMemberKind.Active, rows));
                activeCount++;
            }
        }
        else
        {
            while (TryPeekUtf8(cursor, 0x23A9E6))
            {
                if (activeCount >= 3)
                {
                    diagnostic = "The touch top menu rendered more than three active members.";
                    return false;
                }
                var name = renderObservations[cursor++].Text!;
                if (!TryParseCompactRows(ref cursor, out var rows, out diagnostic))
                {
                    return false;
                }
                members.Add(new ParsedMember(name, TopMenuMemberKind.Active, rows));
                activeCount++;
            }
        }

        while (TryPeekUtf8(cursor, 0x23973B))
        {
            if (reserveCount >= 6)
            {
                diagnostic = "The top menu rendered more than six reserve member blocks.";
                return false;
            }
            if (!TryParseCompactRows(ref cursor, out var rows, out diagnostic))
            {
                return false;
            }
            members.Add(new ParsedMember(null, TopMenuMemberKind.Reserve, rows));
            reserveCount++;
        }

        if (members.Count is < 1 or > 9)
        {
            diagnostic = "The top menu requires one through nine complete character blocks.";
            return false;
        }

        // Touch StatusBar construction can render its own caption before the row builder.
        if (style == TopMenuStyle.Touch && TryTakeUtf8(ref cursor, 0x22ECEB, out var initialCaption))
        {
            renderedFooter.Add(initialCaption);
        }
        var captionRva = style == TopMenuStyle.Classic ? 0x1D179Eu : 0x2221C6u;
        for (var index = 0; index < RowCount; index++)
        {
            if (!TryTakeUtf8(ref cursor, captionRva, out var caption))
            {
                diagnostic = "The top menu does not contain exactly seven localized row captions in native order.";
                return false;
            }
            captions.Add(caption);
        }

        if (style == TopMenuStyle.Classic)
        {
            if (!TryTakeStatus(ref cursor))
            {
                diagnostic = "The classic top menu did not format StatusBar content after rows and before time/currency.";
                return false;
            }
            if (!TryTakeUtf8(ref cursor, 0x1D0AAA, out time) ||
                !TryTakeUtf8(ref cursor, 0x1D0B54, out currency))
            {
                diagnostic = "The classic top menu is missing ordered time/currency observations.";
                return false;
            }
            // The native builder splits the rendered footer into either one line or
            // exactly two, then optionally renders a separate context line.
            if (TryTakeUtf8(ref cursor, 0x1D0D78, out var singleLine))
            {
                renderedFooter.Add(singleLine);
            }
            else if (TryTakeUtf8(ref cursor, 0x1D0DBC, out var firstLine))
            {
                if (!TryTakeUtf8(ref cursor, 0x1D0E05, out var secondLine))
                {
                    diagnostic = "The classic top-menu two-line footer is incomplete.";
                    return false;
                }
                renderedFooter.Add(firstLine);
                renderedFooter.Add(secondLine);
            }
            if (TryTakeUtf8(ref cursor, 0x1D0ED3, out var contextLine))
            {
                renderedFooter.Add(contextLine);
            }
        }
        else
        {
            if (!TryTakeUtf8(ref cursor, 0x221A4F, out time) ||
                !TryTakeUtf8(ref cursor, 0x221B1C, out currency))
            {
                diagnostic = "The touch top menu is missing ordered time/currency observations.";
                return false;
            }
            if (!TryTakeStatus(ref cursor))
            {
                diagnostic = "The touch top menu did not format StatusBar content after time/currency.";
                return false;
            }
        }

        if (cursor != renderObservations.Count)
        {
            diagnostic = "The top-menu render sequence contains extra, duplicate, or out-of-order observations.";
            return false;
        }
        diagnostic = string.Empty;
        return true;
    }

    private bool TryParseClassicRows(
        ref int cursor,
        out IReadOnlyList<TopMenuStatRowSnapshot> rows,
        out string diagnostic)
    {
        var parsed = new List<TopMenuStatRowSnapshot>(3);
        if (!TryTakeUtf8(ref cursor, 0x23A2F2, out var rowZeroLabel) ||
            !TryTakeUtf8(ref cursor, 0x23A552, out var rowZeroValue))
        {
            rows = Array.Empty<TopMenuStatRowSnapshot>();
            diagnostic = "A classic active block has an incomplete row-zero label/value branch.";
            return false;
        }
        var rowZeroExtra = TryTakeOptionalUtf8(ref cursor, 0x23A73B);
        parsed.Add(new TopMenuStatRowSnapshot(rowZeroLabel, [rowZeroValue], rowZeroExtra));

        for (var row = 1; row < 3; row++)
        {
            if (!TryTakeUtf8(ref cursor, 0x23A2F2, out var label))
            {
                rows = Array.Empty<TopMenuStatRowSnapshot>();
                diagnostic = "A classic active block is missing a later-row localized label.";
                return false;
            }

            IReadOnlyList<string> tokens;
            if (TryTakeUtf8(ref cursor, 0x23A3C2, out var placeholder))
            {
                tokens = [placeholder];
            }
            else if (TryTakeUtf8(ref cursor, 0x23A5EF, out var current) &&
                     TryTakeUtf8(ref cursor, 0x23A697, out var maximum))
            {
                tokens = [current, maximum];
            }
            else
            {
                rows = Array.Empty<TopMenuStatRowSnapshot>();
                diagnostic = "A classic active later row has neither the placeholder nor ordered current/maximum branch.";
                return false;
            }
            if (!TryTakeUtf8(ref cursor, 0x23A73B, out var extra))
            {
                rows = Array.Empty<TopMenuStatRowSnapshot>();
                diagnostic = "A classic active later row is missing its mandatory extra visible token.";
                return false;
            }
            parsed.Add(new TopMenuStatRowSnapshot(label, tokens, extra));
        }

        rows = new ReadOnlyCollection<TopMenuStatRowSnapshot>(parsed);
        diagnostic = string.Empty;
        return true;
    }

    private bool TryParseCompactRows(
        ref int cursor,
        out IReadOnlyList<TopMenuStatRowSnapshot> rows,
        out string diagnostic)
    {
        var parsed = new List<TopMenuStatRowSnapshot>(3);
        if (!TryTakeUtf8(ref cursor, 0x23973B, out var rowZeroLabel) ||
            !TryTakeUtf8(ref cursor, 0x239891, out var rowZeroValue))
        {
            rows = Array.Empty<TopMenuStatRowSnapshot>();
            diagnostic = "A compact member block has an incomplete row-zero label/single-value branch.";
            return false;
        }
        var rowZeroExtra = TryTakeOptionalUtf8(ref cursor, 0x239A73);
        parsed.Add(new TopMenuStatRowSnapshot(rowZeroLabel, [rowZeroValue], rowZeroExtra));

        for (var row = 1; row < 3; row++)
        {
            if (!TryTakeUtf8(ref cursor, 0x23973B, out var label) ||
                !TryTakeUtf8(ref cursor, 0x239914, out var current) ||
                !TryTakeUtf8(ref cursor, 0x2399AA, out var maximum) ||
                !TryTakeUtf8(ref cursor, 0x239A73, out var extra))
            {
                rows = Array.Empty<TopMenuStatRowSnapshot>();
                diagnostic = "A compact member block is missing its ordered later-row label/current/maximum/extra grammar.";
                return false;
            }
            parsed.Add(new TopMenuStatRowSnapshot(label, [current, maximum], extra));
        }

        rows = new ReadOnlyCollection<TopMenuStatRowSnapshot>(parsed);
        diagnostic = string.Empty;
        return true;
    }

    private bool TryCaptureRoster(
        IReadOnlyList<ParsedMember> parsedMembers,
        out RosterCapture capture,
        out string diagnostic)
    {
        capture = null!;
        if (!TryAddX86(imageBase, GlobalPointerRva, out var globalPointerAddress) ||
            !TryReadUInt32(memory, globalPointerAddress, out var global) ||
            !FitsAlignedX86Range(global, 1))
        {
            diagnostic = "The top-menu global pointer is unreadable, unaligned, null, or crosses x86 memory.";
            return false;
        }
        if (!TryAddX86(global, GlobalReserveGateOffset, out var gateAddress) ||
            !TryReadByte(memory, gateAddress, out var gate) ||
            !TryAddX86(global, GlobalReserveThresholdOffset, out var thresholdAddress) ||
            !TryReadInt32(memory, thresholdAddress, out var threshold) ||
            !TryAddX86(global, GlobalRosterOwnerOffset, out var ownerAddress) ||
            !TryReadUInt32(memory, ownerAddress, out var rosterOwner) ||
            !FitsAlignedX86Range(rosterOwner, sizeof(uint)))
        {
            diagnostic = "The top-menu global gate, threshold, or party-slot owner is unreadable or outside x86 memory.";
            return false;
        }
        if (!TryAddX86(rosterOwner, RosterSlotsOffset, out var slotsAddress) ||
            !FitsX86Range(slotsAddress, RosterSlotCount * sizeof(uint)))
        {
            diagnostic = "The top-menu nine-slot roster address crosses x86 memory.";
            return false;
        }
        var slotBytes = new byte[RosterSlotCount * sizeof(uint)];
        if (!memory.TryRead(slotsAddress, slotBytes))
        {
            diagnostic = "The top-menu nine-slot roster is unreadable.";
            return false;
        }

        var activeObserved = parsedMembers.Count(member => member.Kind == TopMenuMemberKind.Active);
        var activeExpected = Enumerable.Range(0, 3).Count(slot => IsPresent(ReadUInt32(slotBytes, slot * 4)));
        if (activeObserved != activeExpected)
        {
            diagnostic = $"Observed active member count {activeObserved} does not match roster count {activeExpected}.";
            return false;
        }

        var reserveIds = new List<uint>(6);
        if (gate != 0 || threshold >= 0x49)
        {
            for (var slot = 3; slot < RosterSlotCount; slot++)
            {
                var id = ReadUInt32(slotBytes, slot * 4);
                if (!IsPresent(id))
                {
                    continue;
                }
                if (id > 7)
                {
                    diagnostic = $"Reserve slot {slot} contains invalid full dword portrait ID {id}.";
                    return false;
                }
                if (reserveIds.Contains(id))
                {
                    diagnostic = $"Reserve portrait ID {id} appears more than once.";
                    return false;
                }
                reserveIds.Add(id);
            }
        }

        var reserveObserved = parsedMembers.Count(member => member.Kind == TopMenuMemberKind.Reserve);
        if (reserveObserved != reserveIds.Count)
        {
            diagnostic = $"Observed reserve block count {reserveObserved} does not match displayed roster count {reserveIds.Count}.";
            return false;
        }

        var names = new List<ReserveNameCapture>(reserveIds.Count);
        foreach (var id in reserveIds)
        {
            var nameOffset = checked(ReserveNamesOffset + id * MsvcStringReader.LayoutSize);
            if (!TryAddX86(global, nameOffset, out var nameAddress))
            {
                diagnostic = "A reserve runtime-name address crosses x86 memory.";
                return false;
            }
            if (!TryCaptureReserveName(nameAddress, out var name, out diagnostic))
            {
                return false;
            }
            names.Add(name);
        }

        capture = new RosterCapture(
            globalPointerAddress,
            global,
            gateAddress,
            gate,
            thresholdAddress,
            threshold,
            ownerAddress,
            rosterOwner,
            slotsAddress,
            slotBytes,
            new ReadOnlyCollection<ReserveNameCapture>(names));
        diagnostic = string.Empty;
        return true;
    }

    private bool TryCaptureReserveName(
        uint nameAddress,
        out ReserveNameCapture capture,
        out string diagnostic)
    {
        capture = null!;
        if (!FitsX86Range(nameAddress, MsvcStringReader.LayoutSize))
        {
            diagnostic = "A reserve runtime-name header crosses x86 memory.";
            return false;
        }
        var header = new byte[MsvcStringReader.LayoutSize];
        if (!memory.TryRead(nameAddress, header))
        {
            diagnostic = "A reserve runtime-name header is unreadable.";
            return false;
        }

        var length = ReadUInt32(header, 0x10);
        var capacity = ReadUInt32(header, 0x14);
        if (length > MaximumReserveNameBytes || capacity < length)
        {
            diagnostic = "A reserve runtime-name has invalid length/capacity metadata.";
            return false;
        }

        uint? dataAddress = null;
        byte[] payload;
        if (capacity < 16)
        {
            payload = header[..checked((int)length)].ToArray();
        }
        else
        {
            var heapAddress = ReadUInt32(header, 0);
            if (!FitsX86Range(heapAddress, checked((int)length)))
            {
                diagnostic = "A heap-backed reserve runtime-name has a null or overflowing x86 data range.";
                return false;
            }
            dataAddress = heapAddress;
            payload = new byte[checked((int)length)];
            if (!memory.TryRead(heapAddress, payload))
            {
                diagnostic = "A heap-backed reserve runtime-name payload is unreadable.";
                return false;
            }
        }

        string value;
        try
        {
            value = StrictUtf8.GetString(payload);
        }
        catch (DecoderFallbackException)
        {
            diagnostic = "A reserve runtime-name is not valid UTF-8.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(value))
        {
            diagnostic = "A reserve portrait has no nonblank runtime/player-renamed identity.";
            return false;
        }

        capture = new ReserveNameCapture(
            nameAddress,
            header,
            dataAddress,
            payload,
            new string(value.AsSpan()));
        diagnostic = string.Empty;
        return true;
    }

    private bool TryCorrelateControls(
        IReadOnlyList<string> captions,
        out IReadOnlyList<MenuControlSnapshot> snapshots,
        out ManagerCapture managerCapture,
        out string diagnostic)
    {
        snapshots = Array.Empty<MenuControlSnapshot>();
        managerCapture = null!;
        if (captions.Count != RowCount || controls.Count != RowCount ||
            bindingByControl.Count != RowCount || bindingManager is null)
        {
            diagnostic = "The top menu requires exactly seven captions, controls, bindings, and one manager.";
            return false;
        }
        if (!controlByPointer.Keys.ToHashSet().SetEquals(bindingByControl.Keys))
        {
            diagnostic = "The top-menu constructed-control and manager-binding pointer sets differ.";
            return false;
        }

        var captured = new List<MenuControlSnapshot>(RowCount);
        for (var index = 0; index < RowCount; index++)
        {
            var control = controls[index];
            if (control.Position != index || !control.Visible ||
                !bindingByControl.TryGetValue(control.Pointer, out var binding))
            {
                diagnostic = "Top-menu controls are not in strict native construction order positions 0..6.";
                return false;
            }
            captured.Add(new MenuControlSnapshot(
                new string(captions[index].AsSpan()),
                null,
                null,
                binding.Key,
                index + 1,
                RowCount,
                control.Enabled,
                control.Visible));
        }

        var manager = checked((uint)bindingManager.Value);
        if (!TryAddX86(manager, ManagerFocusedKeyOffset, out var focusAddress) ||
            !TryReadInt32(memory, focusAddress, out var focusedKey) || focusedKey < 0 ||
            captured.Count(control => control.Key == focusedKey) != 1)
        {
            diagnostic = "The top-menu focused native key is unreadable, negative, or does not match exactly one row binding.";
            return false;
        }

        ClassicManagerStackCapture? classicStack = null;
        if (style == TopMenuStyle.Classic &&
            !TryCaptureClassicManagerStack(manager, out classicStack, out diagnostic))
        {
            return false;
        }

        snapshots = new ReadOnlyCollection<MenuControlSnapshot>(captured);
        managerCapture = new ManagerCapture(manager, focusAddress, focusedKey, classicStack);
        diagnostic = string.Empty;
        return true;
    }

    private bool TryCaptureClassicManagerStack(
        uint manager,
        out ClassicManagerStackCapture? capture,
        out string diagnostic)
    {
        capture = null;
        if (!TryReadRootPointer(ClassicManagerStackOffset, out var managerStack) ||
            !FitsAlignedX86Range(managerStack, 16) ||
            !TryAddX86(managerStack, 4, out var headerAddress))
        {
            diagnostic = "The classic top-menu manager-stack pointer is invalid or unreadable.";
            return false;
        }
        var header = new byte[12];
        if (!memory.TryRead(headerAddress, header))
        {
            diagnostic = "The classic top-menu manager-stack vector header is unreadable.";
            return false;
        }
        if (!TryValidateManagerVectorHeader(header, out var begin, out var byteLength, out diagnostic))
        {
            return false;
        }
        var elements = new byte[byteLength];
        if (!memory.TryRead(begin, elements))
        {
            diagnostic = "The classic top-menu manager-stack elements are unreadable.";
            return false;
        }
        for (var offset = 0; offset < elements.Length; offset += sizeof(uint))
        {
            if (!FitsAlignedX86Range(ReadUInt32(elements, offset), sizeof(uint)))
            {
                diagnostic = "The classic top-menu manager stack contains a null, unaligned, or overflowing manager pointer.";
                return false;
            }
        }
        if (ReadUInt32(elements, byteLength - sizeof(uint)) != manager)
        {
            diagnostic = "The binder-correlated manager is not the last active classic manager-stack entry.";
            return false;
        }

        capture = new ClassicManagerStackCapture(managerStack, headerAddress, header, begin, elements);
        diagnostic = string.Empty;
        return true;
    }

    private static bool TryValidateManagerVectorHeader(
        byte[] header,
        out uint begin,
        out int byteLength,
        out string diagnostic)
    {
        begin = ReadUInt32(header, 0);
        var end = ReadUInt32(header, 4);
        var capacity = ReadUInt32(header, 8);
        byteLength = 0;
        if (begin == 0 || end <= begin || capacity < end ||
            (begin & 3) != 0 || (end & 3) != 0 || (capacity & 3) != 0)
        {
            diagnostic = "The classic manager-stack vector pointers are null, reversed, empty, or unaligned.";
            return false;
        }
        var span = end - begin;
        var capacitySpan = capacity - begin;
        if (span % sizeof(uint) != 0 || capacitySpan % sizeof(uint) != 0 ||
            span / sizeof(uint) > MaximumManagerStackCount ||
            capacitySpan / sizeof(uint) > MaximumManagerStackCount ||
            !FitsX86Range(begin, checked((int)span)))
        {
            diagnostic = "The classic manager-stack vector exceeds its exact stride, x86 range, or 16-entry bound.";
            return false;
        }
        byteLength = checked((int)span);
        diagnostic = string.Empty;
        return true;
    }

    private bool TryRevalidateManager(ManagerCapture capture, out string diagnostic)
    {
        if (!TryReadInt32(memory, capture.FocusAddress, out var focusedKey) ||
            focusedKey != capture.FocusedKey)
        {
            diagnostic = "The top-menu focused key changed while its snapshot was being captured.";
            return false;
        }
        if (capture.ClassicStack is null)
        {
            diagnostic = string.Empty;
            return true;
        }

        if (!TryReadRootPointer(ClassicManagerStackOffset, out var managerStack) ||
            managerStack != capture.ClassicStack.ManagerStack)
        {
            diagnostic = "The classic manager-stack owner pointer changed while capturing.";
            return false;
        }
        var header = new byte[capture.ClassicStack.Header.Length];
        var elements = new byte[capture.ClassicStack.Elements.Length];
        if (!memory.TryRead(capture.ClassicStack.HeaderAddress, header) ||
            !header.AsSpan().SequenceEqual(capture.ClassicStack.Header) ||
            !memory.TryRead(capture.ClassicStack.Begin, elements) ||
            !elements.AsSpan().SequenceEqual(capture.ClassicStack.Elements))
        {
            diagnostic = "The classic manager-stack vector changed or became unreadable while capturing.";
            return false;
        }
        diagnostic = string.Empty;
        return true;
    }

    private bool TryRevalidateRoster(RosterCapture capture, out string diagnostic)
    {
        if (!TryReadUInt32(memory, capture.GlobalPointerAddress, out var global) || global != capture.Global ||
            !TryReadByte(memory, capture.GateAddress, out var gate) || gate != capture.Gate ||
            !TryReadInt32(memory, capture.ThresholdAddress, out var threshold) || threshold != capture.Threshold ||
            !TryReadUInt32(memory, capture.OwnerAddress, out var owner) || owner != capture.RosterOwner)
        {
            diagnostic = "The global pointer, reserve gate, threshold, or party-slot owner changed while capturing.";
            return false;
        }

        var slots = new byte[capture.SlotBytes.Length];
        if (!memory.TryRead(capture.SlotsAddress, slots) ||
            !slots.AsSpan().SequenceEqual(capture.SlotBytes))
        {
            diagnostic = "The nine party slots changed or became unreadable while capturing.";
            return false;
        }

        foreach (var name in capture.ReserveNames)
        {
            var header = new byte[name.Header.Length];
            if (!memory.TryRead(name.HeaderAddress, header) ||
                !header.AsSpan().SequenceEqual(name.Header))
            {
                diagnostic = "A used reserve runtime-name header changed or became unreadable while capturing.";
                return false;
            }
            if (name.DataAddress is { } dataAddress)
            {
                var payload = new byte[name.Payload.Length];
                if (!memory.TryRead(dataAddress, payload) ||
                    !payload.AsSpan().SequenceEqual(name.Payload))
                {
                    diagnostic = "A used heap reserve runtime-name payload changed or became unreadable while capturing.";
                    return false;
                }
            }
        }
        diagnostic = string.Empty;
        return true;
    }

    private bool TryRevalidateStatusBar(StatusCaptureState state, out string diagnostic)
    {
        if (!TryReadUInt32(memory, state.StatusBar, out var statusVtable) ||
            statusVtable != state.ExpectedVtable)
        {
            diagnostic = "The StatusBar vtable changed while dependent text was being captured.";
            return false;
        }
        if (style == TopMenuStyle.Classic &&
            (!TryReadRootPointer(ClassicStatusBarOffset, out var ownedStatusBar) ||
             ownedStatusBar != state.StatusBar))
        {
            diagnostic = "The classic root StatusBar owner pointer changed while capturing.";
            return false;
        }
        diagnostic = string.Empty;
        return true;
    }

    private static IReadOnlyList<string> FlattenStatus(
        IReadOnlyList<TopMenuMemberSnapshot> members,
        string time,
        string currency,
        IReadOnlyList<string> conditionalLines)
    {
        var flattened = new List<string>();
        foreach (var member in members)
        {
            flattened.Add(new string(member.Name.AsSpan()));
            foreach (var row in member.Rows)
            {
                flattened.Add(new string(row.Label.AsSpan()));
                flattened.AddRange(row.ValueTokens.Select(token => new string(token.AsSpan())));
                if (row.Extra is not null)
                {
                    flattened.Add(new string(row.Extra.AsSpan()));
                }
            }
        }
        flattened.Add(new string(time.AsSpan()));
        flattened.Add(new string(currency.AsSpan()));
        flattened.AddRange(conditionalLines.Select(line => new string(line.AsSpan())));
        return new ReadOnlyCollection<string>(flattened);
    }

    private bool IsAllowedUtf8ReturnRva(uint rva)
    {
        if (rva is 0x23973B or 0x239891 or 0x239914 or 0x2399AA or 0x239A73)
        {
            return true;
        }
        return style switch
        {
            TopMenuStyle.Classic => rva is
                0x1D0AAA or 0x1D0B54 or 0x1D179E or 0x23B1D0 or
                0x23A2F2 or 0x23A552 or 0x23A3C2 or 0x23A5EF or 0x23A697 or 0x23A73B or
                0x1D0D78 or 0x1D0DBC or 0x1D0E05 or 0x1D0ED3,
            TopMenuStyle.Touch => rva is 0x221A4F or 0x221B1C or 0x2221C6 or 0x23A9E6 or 0x22ECEB,
            _ => false,
        };
    }

    private bool TryNormalizeReturnAddress(nuint absoluteReturnAddress, out uint rva)
    {
        if (!FitsX86Address(absoluteReturnAddress) || absoluteReturnAddress <= imageBase)
        {
            rva = 0;
            return false;
        }
        rva = checked((uint)absoluteReturnAddress - imageBase);
        return true;
    }

    private bool TryPeekUtf8(int cursor, uint rva) =>
        cursor < renderObservations.Count &&
        renderObservations[cursor].Kind == RenderObservationKind.Utf8 &&
        renderObservations[cursor].Rva == rva;

    private bool TryTakeUtf8(ref int cursor, uint rva, out string value)
    {
        if (TryPeekUtf8(cursor, rva))
        {
            value = renderObservations[cursor++].Text!;
            return true;
        }
        value = string.Empty;
        return false;
    }

    private string? TryTakeOptionalUtf8(ref int cursor, uint rva) =>
        TryTakeUtf8(ref cursor, rva, out var value) ? value : null;

    private bool TryTakeStatus(ref int cursor)
    {
        if (cursor + 1 < renderObservations.Count &&
            renderObservations[cursor].Kind == RenderObservationKind.StatusBegin &&
            renderObservations[cursor + 1].Kind == RenderObservationKind.StatusComplete &&
            ReferenceEquals(renderObservations[cursor].Status, statusState) &&
            ReferenceEquals(renderObservations[cursor + 1].Status, statusState))
        {
            cursor += 2;
            return true;
        }
        return false;
    }

    private bool TryReadRootPointer(uint offset, out uint value)
    {
        value = 0;
        return TryAddX86(root, offset, out var address) &&
            TryReadUInt32(memory, address, out value);
    }

    private bool RecordFailure(string message, out string diagnostic)
    {
        diagnostic = string.IsNullOrWhiteSpace(message) ? "Top-menu capture failed without a diagnostic." : message;
        errors.Add(diagnostic);
        return false;
    }

    private bool IsAbandoned => Volatile.Read(ref abandoned) != 0;

    private bool TryUse(out string diagnostic)
    {
        if (Environment.CurrentManagedThreadId != owningThreadId)
        {
            diagnostic = "Top-menu capture scope access was attempted from a different thread.";
            return false;
        }
        if (disposed)
        {
            diagnostic = "Top-menu capture scope has been disposed.";
            return false;
        }
        if (IsAbandoned)
        {
            diagnostic = "Top-menu capture scope was abandoned after a cross-thread disposal attempt.";
            return false;
        }
        diagnostic = string.Empty;
        return true;
    }

    private static bool IsPresent(uint slotValue) => ((byte)slotValue & 0x80) == 0;

    private static bool FitsX86Address(nuint address) => address != 0 && address <= uint.MaxValue;

    private static bool FitsX86Range(nuint address, int byteLength) =>
        address != 0 && byteLength > 0 && address <= uint.MaxValue &&
        (ulong)address + (uint)byteLength - 1 <= uint.MaxValue;

    private static bool FitsAlignedX86Range(nuint address, int byteLength) =>
        (address & 3) == 0 && FitsX86Range(address, byteLength);

    private static bool TryAddX86(nuint address, uint offset, out uint result)
    {
        var sum = (ulong)address + offset;
        if (!FitsX86Address(address) || sum > uint.MaxValue)
        {
            result = 0;
            return false;
        }
        result = checked((uint)sum);
        return true;
    }

    private static bool TryReadUInt32(IReadableMemory memory, nuint address, out uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        if (!FitsX86Range(address, sizeof(uint)) || !memory.TryRead(address, bytes))
        {
            value = 0;
            return false;
        }
        value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        return true;
    }

    private static bool TryReadInt32(IReadableMemory memory, nuint address, out int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        if (!FitsX86Range(address, sizeof(int)) || !memory.TryRead(address, bytes))
        {
            value = 0;
            return false;
        }
        value = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        return true;
    }

    private static bool TryReadByte(IReadableMemory memory, nuint address, out byte value)
    {
        Span<byte> bytes = stackalloc byte[1];
        if (!FitsX86Range(address, 1) || !memory.TryRead(address, bytes))
        {
            value = 0;
            return false;
        }
        value = bytes[0];
        return true;
    }

    private static uint ReadUInt32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));

    private sealed record RenderObservation(
        RenderObservationKind Kind,
        uint Rva,
        string? Text,
        StatusCaptureState? Status)
    {
        public static RenderObservation Utf8(uint rva, string text) =>
            new(RenderObservationKind.Utf8, rva, text, null);

        public static RenderObservation StatusBegin(StatusCaptureState state) =>
            new(RenderObservationKind.StatusBegin, 0, null, state);

        public static RenderObservation StatusComplete(StatusCaptureState state) =>
            new(RenderObservationKind.StatusComplete, 0, null, state);
    }

    private enum RenderObservationKind
    {
        Utf8,
        StatusBegin,
        StatusComplete,
    }

    private sealed record ConstructedControl(nuint Pointer, int Position, bool Enabled, bool Visible);
    private sealed record Binding(nuint Manager, int Key);
    private sealed record ParsedMember(
        string? Name,
        TopMenuMemberKind Kind,
        IReadOnlyList<TopMenuStatRowSnapshot> Rows);
    private sealed record ReserveNameCapture(
        uint HeaderAddress,
        byte[] Header,
        uint? DataAddress,
        byte[] Payload,
        string Value);
    private sealed record RosterCapture(
        uint GlobalPointerAddress,
        uint Global,
        uint GateAddress,
        byte Gate,
        uint ThresholdAddress,
        int Threshold,
        uint OwnerAddress,
        uint RosterOwner,
        uint SlotsAddress,
        byte[] SlotBytes,
        IReadOnlyList<ReserveNameCapture> ReserveNames);
    private sealed record ClassicManagerStackCapture(
        uint ManagerStack,
        uint HeaderAddress,
        byte[] Header,
        uint Begin,
        byte[] Elements);
    private sealed record ManagerCapture(
        uint Manager,
        uint FocusAddress,
        int FocusedKey,
        ClassicManagerStackCapture? ClassicStack);

    internal sealed class StatusCaptureState(uint statusBar, uint expectedVtable)
    {
        public uint StatusBar { get; } = statusBar;
        public uint ExpectedVtable { get; } = expectedVtable;
        public List<string> Lines { get; } = [];
        public int AggregateCodeUnits { get; set; }
        public bool Completed { get; set; }
        public bool Disposed { get; set; }
        public int Abandoned;
        public bool IsAbandoned => Volatile.Read(ref Abandoned) != 0;
    }
}

public sealed class TopMenuStatusCaptureScope : IDisposable
{
    private readonly TopMenuCaptureScope owner;
    private readonly TopMenuCaptureScope.StatusCaptureState state;
    private readonly int owningThreadId;
    private bool disposed;

    internal TopMenuStatusCaptureScope(
        TopMenuCaptureScope owner,
        TopMenuCaptureScope.StatusCaptureState state,
        int owningThreadId)
    {
        this.owner = owner;
        this.state = state;
        this.owningThreadId = owningThreadId;
    }

    public bool TryRecordRenderedLine(
        nuint absoluteReturnAddress,
        nuint wideStringAddress,
        out string diagnostic) =>
        owner.TryRecordStatusLine(state, absoluteReturnAddress, wideStringAddress, out diagnostic);

    public bool TryComplete(out string diagnostic) =>
        owner.TryCompleteStatus(state, out diagnostic);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        if (Environment.CurrentManagedThreadId != owningThreadId)
        {
            owner.DisposeStatus(state, owningThreadId);
            return;
        }
        disposed = true;
        owner.DisposeStatus(state, owningThreadId);
    }
}

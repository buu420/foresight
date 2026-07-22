using System.Collections.ObjectModel;

namespace ChronoTriggerAccessibility.Native.Capture;

public sealed record ExtrasHubControlInput(
    int Position,
    int Key,
    string Label,
    string? Help,
    bool NativeAvailable,
    bool Visible);

public sealed class ExtrasHubSnapshot
{
    public ExtrasHubSnapshot(IEnumerable<MenuControlSnapshot> controls)
    {
        ArgumentNullException.ThrowIfNull(controls);
        Controls = new ReadOnlyCollection<MenuControlSnapshot>(controls.ToArray());
    }

    public IReadOnlyList<MenuControlSnapshot> Controls { get; }

    public bool TryGetLockedActivationHelp(int key, out string help)
    {
        var control = Controls.SingleOrDefault(item => item.Key == key);
        if (control is null || control.Enabled || string.IsNullOrWhiteSpace(control.Help))
        {
            help = string.Empty;
            return false;
        }

        help = control.Help;
        return true;
    }
}

public sealed record EndingLogRowInput(int Position, string VisibleLabel, bool Locked);

public sealed record EndingLogRowSnapshot(int Position, string VisibleLabel, bool Locked);

public sealed record ExtrasBackControlInput(int Key, string Label, bool NativeAvailable, bool Visible);

public sealed class EndingLogSnapshot
{
    public EndingLogSnapshot(IEnumerable<EndingLogRowSnapshot> rows, MenuControlSnapshot back)
    {
        ArgumentNullException.ThrowIfNull(rows);
        Rows = new ReadOnlyCollection<EndingLogRowSnapshot>(rows.ToArray());
        Back = back ?? throw new ArgumentNullException(nameof(back));
    }

    public IReadOnlyList<EndingLogRowSnapshot> Rows { get; }
    public MenuControlSnapshot Back { get; }
}

public sealed record EndingDetailControlInput(int Key, string Label, bool NativeAvailable, bool Visible);

public sealed class EndingDetailInput
{
    public EndingDetailInput(
        string? visibleTitle,
        string? visibleRequirementsLabel,
        string? visibleRequirementText,
        IEnumerable<EndingDetailControlInput>? controls)
    {
        VisibleTitle = visibleTitle is null ? string.Empty : new string(visibleTitle.AsSpan());
        VisibleRequirementsLabel = visibleRequirementsLabel is null ? string.Empty : new string(visibleRequirementsLabel.AsSpan());
        VisibleRequirementText = visibleRequirementText is null ? string.Empty : new string(visibleRequirementText.AsSpan());
        Controls = new ReadOnlyCollection<EndingDetailControlInput>((controls ?? []).ToArray());
    }

    public string VisibleTitle { get; }
    public string VisibleRequirementsLabel { get; }
    public string VisibleRequirementText { get; }
    public IReadOnlyList<EndingDetailControlInput> Controls { get; }
}

public sealed class EndingDetailSnapshot
{
    public EndingDetailSnapshot(
        string visibleTitle,
        string visibleRequirementsLabel,
        string visibleRequirementText,
        IEnumerable<MenuControlSnapshot> controls)
    {
        ArgumentNullException.ThrowIfNull(controls);
        VisibleTitle = new string(visibleTitle.AsSpan());
        VisibleRequirementsLabel = new string(visibleRequirementsLabel.AsSpan());
        VisibleRequirementText = new string(visibleRequirementText.AsSpan());
        Controls = new ReadOnlyCollection<MenuControlSnapshot>(controls.ToArray());
    }

    public string VisibleTitle { get; }
    public string VisibleRequirementsLabel { get; }
    public string VisibleRequirementText { get; }
    public IReadOnlyList<MenuControlSnapshot> Controls { get; }
}

public static class ExtrasCapture
{
    public const uint ExtrasHubVtableRva = 0x3A5970;
    public const uint EndingLogVtableRva = 0x3A4AE8;
    public const uint EndingDetailVtableRva = 0x3A475C;

    public static bool TryCaptureHub(
        nuint imageBase,
        nuint observedVtable,
        IReadOnlyList<ExtrasHubControlInput>? controls,
        out ExtrasHubSnapshot snapshot,
        out string diagnostic)
    {
        snapshot = null!;
        if (!TryValidateVtable(imageBase, observedVtable, ExtrasHubVtableRva, out diagnostic))
        {
            return false;
        }
        if (controls is null || controls.Count != 5 || controls.Any(control => control is null))
        {
            diagnostic = "Extras Hub requires exactly five visible controls.";
            return false;
        }
        if (controls.Select(control => control.Position).Distinct().Count() != 5 ||
            controls.Select(control => control.Key).Distinct().Count() != 5)
        {
            diagnostic = "Extras Hub controls have duplicate positions or keys.";
            return false;
        }

        var ordered = controls.OrderBy(control => control.Position).ToArray();
        for (var position = 0; position < ordered.Length; position++)
        {
            var control = ordered[position];
            if (control.Position != position || control.Key != position || !control.Visible || string.IsNullOrWhiteSpace(control.Label))
            {
                diagnostic = "Extras Hub controls must have unique ordered keys and positions 0..4 with visible localized labels.";
                return false;
            }
            if (position < 4 && string.IsNullOrWhiteSpace(control.Help))
            {
                diagnostic = "Extras Hub positions 0..3 require localized help and a captured native availability state.";
                return false;
            }
            if (position == 4 && !control.NativeAvailable)
            {
                diagnostic = "Extras Hub Back must be captured as available and visible.";
                return false;
            }
        }

        snapshot = new ExtrasHubSnapshot(ordered.Select((control, position) => new MenuControlSnapshot(
            new string(control.Label.AsSpan()),
            null,
            control.Help is null ? null : new string(control.Help.AsSpan()),
            control.Key,
            position + 1,
            ordered.Length,
            control.NativeAvailable,
            control.Visible)));
        diagnostic = string.Empty;
        return true;
    }

    public static bool TryCaptureEndingLog(
        nuint imageBase,
        nuint observedVtable,
        IReadOnlyList<EndingLogRowInput>? rows,
        ExtrasBackControlInput? back,
        out EndingLogSnapshot snapshot,
        out string diagnostic)
    {
        snapshot = null!;
        if (!TryValidateVtable(imageBase, observedVtable, EndingLogVtableRva, out diagnostic))
        {
            return false;
        }
        if (rows is null || rows.Count != 19 || rows.Any(row => row is null))
        {
            diagnostic = "Ending Log requires exactly nineteen captured ending rows.";
            return false;
        }
        if (rows.Select(row => row.Position).Distinct().Count() != 19)
        {
            diagnostic = "Ending Log rows have duplicate positions.";
            return false;
        }

        var orderedRows = rows.OrderBy(row => row.Position).ToArray();
        for (var position = 0; position < orderedRows.Length; position++)
        {
            var row = orderedRows[position];
            if (row.Position != position || string.IsNullOrWhiteSpace(row.VisibleLabel) ||
                (row.Locked && row.VisibleLabel != "???") || (!row.Locked && row.VisibleLabel == "???"))
            {
                diagnostic = "Ending Log rows must be ordered 0..18 with nonblank visible labels; only locked rows retain the visible ??? label.";
                return false;
            }
        }
        if (back is null || back.Key < 0 || back.Key <= 18 || string.IsNullOrWhiteSpace(back.Label) || !back.NativeAvailable || !back.Visible)
        {
            diagnostic = "Ending Log requires a separately captured available visible Back control with a non-colliding manager key.";
            return false;
        }

        snapshot = new EndingLogSnapshot(
            orderedRows.Select(row => new EndingLogRowSnapshot(row.Position, new string(row.VisibleLabel.AsSpan()), row.Locked)),
            new MenuControlSnapshot(new string(back.Label.AsSpan()), null, null, back.Key, 20, 20, back.NativeAvailable, back.Visible));
        diagnostic = string.Empty;
        return true;
    }

    public static bool TryCaptureEndingDetail(
        nuint imageBase,
        nuint observedVtable,
        EndingDetailInput? input,
        out EndingDetailSnapshot snapshot,
        out string diagnostic)
    {
        snapshot = null!;
        if (!TryValidateVtable(imageBase, observedVtable, EndingDetailVtableRva, out diagnostic))
        {
            return false;
        }
        if (input is null || string.IsNullOrWhiteSpace(input.VisibleTitle) ||
            string.IsNullOrWhiteSpace(input.VisibleRequirementsLabel) ||
            string.IsNullOrWhiteSpace(input.VisibleRequirementText) ||
            input.Controls is null || input.Controls.Count != 2 || input.Controls.Any(control => control is null))
        {
            diagnostic = "Ending Detail requires nonblank visible title, Requirements label, requirement text, and two controls.";
            return false;
        }
        if (input.Controls.Select(control => control.Key).Distinct().Count() != 2)
        {
            diagnostic = "Ending Detail controls have duplicate keys.";
            return false;
        }

        var controls = input.Controls.OrderBy(control => control.Key).ToArray();
        for (var key = 0; key < controls.Length; key++)
        {
            var control = controls[key];
            if (control.Key != key || string.IsNullOrWhiteSpace(control.Label) || !control.NativeAvailable || !control.Visible)
            {
                diagnostic = "Ending Detail requires exact Review key 0 and Back key 1 with visible available localized labels.";
                return false;
            }
        }

        snapshot = new EndingDetailSnapshot(
            input.VisibleTitle,
            input.VisibleRequirementsLabel,
            input.VisibleRequirementText,
            controls.Select((control, position) => new MenuControlSnapshot(
                new string(control.Label.AsSpan()),
                null,
                null,
                control.Key,
                position + 1,
                controls.Length,
                control.NativeAvailable,
                control.Visible)));
        diagnostic = string.Empty;
        return true;
    }

    private static bool TryValidateVtable(nuint imageBase, nuint observedVtable, uint expectedRva, out string diagnostic)
    {
        if (imageBase == 0 || (ulong)imageBase > uint.MaxValue || (ulong)observedVtable > uint.MaxValue)
        {
            diagnostic = "The observed vtable or image base is outside the audited x86 address range.";
            return false;
        }

        var expected = (ulong)imageBase + expectedRva;
        if (expected > uint.MaxValue || observedVtable != (nuint)expected)
        {
            diagnostic = "The observed vtable does not equal the audited image base plus expected RVA.";
            return false;
        }

        diagnostic = string.Empty;
        return true;
    }
}

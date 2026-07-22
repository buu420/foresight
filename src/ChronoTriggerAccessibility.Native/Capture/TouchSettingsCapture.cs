using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

public static class TouchSettingsCapture
{
    public const uint RootVtableRva = 0x3A6BC4;
    public const uint PagerVtableRva = 0x3AC940;
    public const int DescriptorCount = 4;
    public const int MaximumRowCount = 64;

    private const int RootSize = 0x2FC;
    private const int DescriptorVectorOffset = 0x2C8;
    private const int PagerPointerOffset = 0x2D4;
    private const int Control1001PointerOffset = 0x2DC;
    private const int Control1002PointerOffset = 0x2E0;
    private const int Control1000PointerOffset = 0x2E4;
    private const int ActivePageOffset = 0x2E8;
    private const int GroupVectorOffset = 0x2EC;
    private const int FocusKeyOffset = 0x2F8;
    private const int PagerSize = 0x2E2;
    private const int PagerPageCountOffset = 0x2D0;
    private const int PagerActivePageOffset = 0x2D4;
    private const int PagerActiveRootOffset = 0x2D8;
    private const int PagerTransitionOffset = 0x2E1;
    private const int DescriptorStride = 0x0C;
    private const int GroupStride = 0x10;
    private const int RowStride = 0x98;
    private const int RowUiTypeOffset = 0x00;
    private const int RowLabelOffset = 0x04;
    private const int RowHelpOffset = 0x1C;
    private const int RowValuesOffset = 0x34;
    private const int RowSelectedIndexOffset = 0x90;

    public static bool TryCreateSnapshot(
        IReadableMemory? memory,
        nuint imageBase,
        nuint root,
        IReadOnlyList<TouchSettingsSpecialControlObservation>? specialControls,
        out TouchSettingsSnapshot snapshot,
        out string diagnostic) =>
        TryCreateSnapshot(memory, imageBase, root, specialControls, allowActiveTransition: false, out snapshot, out diagnostic);

    public static bool TryCreateTransitionSnapshot(
        IReadableMemory? memory,
        nuint imageBase,
        nuint root,
        IReadOnlyList<TouchSettingsSpecialControlObservation>? specialControls,
        out TouchSettingsSnapshot snapshot,
        out string diagnostic) =>
        TryCreateSnapshot(memory, imageBase, root, specialControls, allowActiveTransition: true, out snapshot, out diagnostic);

    private static bool TryCreateSnapshot(
        IReadableMemory? memory,
        nuint imageBase,
        nuint root,
        IReadOnlyList<TouchSettingsSpecialControlObservation>? specialControls,
        bool allowActiveTransition,
        out TouchSettingsSnapshot snapshot,
        out string diagnostic)
    {
        try
        {
            return TryCreateSnapshotCore(
                memory,
                imageBase,
                root,
                specialControls,
                allowActiveTransition,
                out snapshot,
                out diagnostic);
        }
        catch (Exception exception)
        {
            snapshot = null!;
            diagnostic = $"Touch Settings capture failed safely: {exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    private static bool TryCreateSnapshotCore(
        IReadableMemory? memory,
        nuint imageBase,
        nuint root,
        IReadOnlyList<TouchSettingsSpecialControlObservation>? specialControls,
        bool allowActiveTransition,
        out TouchSettingsSnapshot snapshot,
        out string diagnostic)
    {
        snapshot = null!;
        if (memory is null || !SettingsCaptureMemory.FitsX86Range(root, RootSize))
        {
            diagnostic = "Touch Settings memory or root is unavailable or outside the x86 address space.";
            return false;
        }
        if (specialControls is null || specialControls.Any(control => control is null))
        {
            diagnostic = "Touch Settings requires a non-null special-control observation collection.";
            return false;
        }
        if (!SettingsCaptureMemory.TryResolveVtable(imageBase, RootVtableRva, out var expectedRootVtable, out diagnostic) ||
            !SettingsCaptureMemory.TryResolveVtable(imageBase, PagerVtableRva, out var expectedPagerVtable, out diagnostic))
        {
            return false;
        }
        if (!SettingsCaptureMemory.TryReadBytes(memory, root, RootSize, "Touch Settings root", out var rootBytes, out diagnostic))
        {
            return false;
        }
        if (SettingsCaptureMemory.ReadUInt32(rootBytes, 0) != expectedRootVtable)
        {
            diagnostic = "Touch Settings root vtable does not match MenuNodeConfig.";
            return false;
        }

        if (!SettingsCaptureMemory.TryReadVector(
                rootBytes.AsSpan(DescriptorVectorOffset, SettingsCaptureMemory.VectorHeaderSize),
                DescriptorStride,
                DescriptorCount,
                allowEmpty: false,
                "Touch Settings descriptor",
                out var descriptorVector,
                out diagnostic) ||
            descriptorVector.Count != DescriptorCount)
        {
            if (string.IsNullOrWhiteSpace(diagnostic))
            {
                diagnostic = $"Touch Settings requires exactly {DescriptorCount} descriptors.";
            }
            return false;
        }
        if (!SettingsCaptureMemory.TryReadVector(
                rootBytes.AsSpan(GroupVectorOffset, SettingsCaptureMemory.VectorHeaderSize),
                GroupStride,
                MaximumRowCount,
                allowEmpty: false,
                "Touch Settings active control-group",
                out var groupVector,
                out diagnostic))
        {
            return false;
        }

        var pagerAddress = SettingsCaptureMemory.ReadUInt32(rootBytes, PagerPointerOffset);
        if (!SettingsCaptureMemory.FitsX86Range(pagerAddress, PagerSize))
        {
            diagnostic = "Touch Settings ConfigPager pointer is null or crosses the x86 address space.";
            return false;
        }
        if (!SettingsCaptureMemory.TryReadBytes(memory, pagerAddress, PagerSize, "Touch Settings ConfigPager", out var pagerBytes, out diagnostic))
        {
            return false;
        }
        if (SettingsCaptureMemory.ReadUInt32(pagerBytes, 0) != expectedPagerVtable)
        {
            diagnostic = "Touch Settings ConfigPager vtable does not match the audited pager type.";
            return false;
        }

        var pageCount = SettingsCaptureMemory.ReadInt32(pagerBytes, PagerPageCountOffset);
        var pagerActivePage = SettingsCaptureMemory.ReadInt32(pagerBytes, PagerActivePageOffset);
        var rootActivePage = SettingsCaptureMemory.ReadInt32(rootBytes, ActivePageOffset);
        var activePageRoot = SettingsCaptureMemory.ReadUInt32(pagerBytes, PagerActiveRootOffset);
        var transition = pagerBytes[PagerTransitionOffset];
        if (pageCount != DescriptorCount)
        {
            diagnostic = $"Touch Settings pager page count {pageCount} does not match the exact four descriptors.";
            return false;
        }
        if (rootActivePage < 0 || rootActivePage >= DescriptorCount || pagerActivePage != rootActivePage)
        {
            diagnostic = "Touch Settings root and pager do not identify one matching valid active page.";
            return false;
        }
        if (!SettingsCaptureMemory.FitsX86Address(activePageRoot))
        {
            diagnostic = "Touch Settings pager active page root is null or outside x86 memory.";
            return false;
        }
        if (transition > 1 || (!allowActiveTransition && transition != 0))
        {
            diagnostic = allowActiveTransition
                ? $"Touch Settings pager transition byte {transition} is outside the audited 0/1 values."
                : $"Touch Settings stable capture rejects active pager transition byte {transition}.";
            return false;
        }

        if (!SettingsCaptureMemory.TryReadBytes(
                memory,
                descriptorVector.Begin,
                descriptorVector.ByteLength,
                "Touch Settings descriptors",
                out var descriptorBytes,
                out diagnostic))
        {
            return false;
        }
        var descriptorOffset = rootActivePage * DescriptorStride;
        if (!SettingsCaptureMemory.TryElementAddress(
                descriptorVector.Begin,
                rootActivePage,
                DescriptorStride,
                out var activeDescriptorAddress) ||
            !SettingsCaptureMemory.TryReadVector(
                descriptorBytes.AsSpan(descriptorOffset, DescriptorStride),
                RowStride,
                MaximumRowCount,
                allowEmpty: false,
                "Touch Settings active row",
                out var rowVector,
                out diagnostic))
        {
            return false;
        }
        if (groupVector.Count != rowVector.Count)
        {
            diagnostic = $"Touch Settings active control-group count {groupVector.Count} does not match row count {rowVector.Count}.";
            return false;
        }
        if (!SettingsCaptureMemory.TryReadBytes(
                memory,
                rowVector.Begin,
                rowVector.ByteLength,
                "Touch Settings active rows",
                out var rowBytes,
                out diagnostic) ||
            !SettingsCaptureMemory.TryReadBytes(
                memory,
                groupVector.Begin,
                groupVector.ByteLength,
                "Touch Settings active control groups",
                out var groupBytes,
                out diagnostic))
        {
            return false;
        }

        var guards = new List<SettingsMemoryGuard>
        {
            new(root, rootBytes, "Touch Settings root"),
            new(pagerAddress, pagerBytes, "Touch Settings ConfigPager"),
            new(descriptorVector.Begin, descriptorBytes, "Touch Settings descriptors"),
            new(activeDescriptorAddress, descriptorBytes.AsSpan(descriptorOffset, DescriptorStride).ToArray(), "Touch Settings active descriptor"),
            new(rowVector.Begin, rowBytes, "Touch Settings active rows"),
            new(groupVector.Begin, groupBytes, "Touch Settings active control groups"),
        };

        var rows = new TouchSettingsRowSnapshot[rowVector.Count];
        for (var index = 0; index < rows.Length; index++)
        {
            if (!TryCaptureRow(
                    memory,
                    rowVector,
                    rowBytes,
                    index,
                    guards,
                    out rows[index],
                    out diagnostic))
            {
                return false;
            }
        }

        if (!TryCaptureSpecialControls(
                rootBytes,
                specialControls,
                out var capturedSpecialControls,
                out diagnostic))
        {
            return false;
        }

        var focusedKey = SettingsCaptureMemory.ReadInt32(rootBytes, FocusKeyOffset);
        TouchSettingsFocusKind focusKind;
        int? focusedRow;
        int? focusedSubcontrol;
        int? focusedSpecial;
        if (focusedKey == -1)
        {
            focusKind = TouchSettingsFocusKind.None;
            focusedRow = null;
            focusedSubcontrol = null;
            focusedSpecial = null;
        }
        else if (focusedKey < -1)
        {
            diagnostic = $"Touch Settings focus key {focusedKey} is below the audited unfocused value -1.";
            return false;
        }
        else if (focusedKey < 1000)
        {
            var rowIndex = focusedKey / 4;
            var subcontrol = focusedKey % 4;
            if (rowIndex < 0 || rowIndex >= rows.Length)
            {
                diagnostic = $"Touch Settings key {focusedKey} decodes to row {rowIndex}, outside {rows.Length} rows.";
                return false;
            }
            var row = rows[rowIndex];
            if (row.UiType == 0 && subcontrol != row.SelectedIndex + 1)
            {
                diagnostic = "Touch Settings type-0 focus must identify the subcontrol for its stored selected value.";
                return false;
            }
            if (row.UiType is 1 or 2 && subcontrol != 1)
            {
                diagnostic = "Touch Settings type-1/type-2 focus must identify the primary row subcontrol.";
                return false;
            }

            focusKind = TouchSettingsFocusKind.Row;
            focusedRow = rowIndex;
            focusedSubcontrol = subcontrol;
            focusedSpecial = null;
        }
        else
        {
            if (focusedKey is < 1000 or > 1002 || capturedSpecialControls.All(control => control.Key != focusedKey))
            {
                diagnostic = $"Touch Settings special focus key {focusedKey} has no separately captured visible control.";
                return false;
            }

            focusKind = TouchSettingsFocusKind.SpecialControl;
            focusedRow = null;
            focusedSubcontrol = null;
            focusedSpecial = focusedKey;
        }

        if (!SettingsCaptureMemory.TryRevalidate(memory, guards, out diagnostic))
        {
            return false;
        }

        snapshot = new TouchSettingsSnapshot(
            pagerAddress,
            activePageRoot,
            rootActivePage,
            pageCount,
            transition == 1,
            groupVector.Count,
            rows,
            capturedSpecialControls,
            focusedKey,
            focusKind,
            focusedRow,
            focusedSubcontrol,
            focusedSpecial);
        diagnostic = string.Empty;
        return true;
    }

    private static bool TryCaptureRow(
        IReadableMemory memory,
        SettingsVector rowVector,
        byte[] rowBytes,
        int index,
        ICollection<SettingsMemoryGuard> guards,
        out TouchSettingsRowSnapshot row,
        out string diagnostic)
    {
        row = null!;
        if (!SettingsCaptureMemory.TryElementAddress(rowVector.Begin, index, RowStride, out var rowAddress) ||
            !SettingsCaptureMemory.TryAddX86(rowAddress, RowLabelOffset, out var labelAddress) ||
            !SettingsCaptureMemory.TryAddX86(rowAddress, RowHelpOffset, out var helpAddress) ||
            !SettingsCaptureMemory.TryAddX86(rowAddress, RowValuesOffset, out var valueHeaderAddress))
        {
            diagnostic = $"Touch Settings row {index} address crosses x86 memory.";
            return false;
        }

        var offset = index * RowStride;
        var uiType = SettingsCaptureMemory.ReadInt32(rowBytes, offset + RowUiTypeOffset);
        if (uiType is < 0 or > 2)
        {
            diagnostic = $"Touch Settings row {index} UI type {uiType} is outside the audited 0..2 range.";
            return false;
        }
        if (!SettingsCaptureMemory.TryReadString(
                memory,
                labelAddress,
                rowBytes.AsSpan(offset + RowLabelOffset, MsvcStringReader.LayoutSize),
                $"Touch Settings row {index} label",
                out var label,
                out diagnostic) ||
            !SettingsCaptureMemory.TryReadString(
                memory,
                helpAddress,
                rowBytes.AsSpan(offset + RowHelpOffset, MsvcStringReader.LayoutSize),
                $"Touch Settings row {index} help",
                out var help,
                out diagnostic) ||
            !SettingsCaptureMemory.TryReadStringVector(
                memory,
                valueHeaderAddress,
                rowBytes.AsSpan(offset + RowValuesOffset, SettingsCaptureMemory.VectorHeaderSize),
                allowEmpty: false,
                $"Touch Settings row {index} values",
                guards,
                out var values,
                out _,
                out diagnostic))
        {
            return false;
        }
        if (uiType is 0 or 2 && values.Count != 2)
        {
            diagnostic = $"Touch Settings type-{uiType} row {index} requires exactly two displayed values.";
            return false;
        }

        var selectedIndex = SettingsCaptureMemory.ReadInt32(rowBytes, offset + RowSelectedIndexOffset);
        if (selectedIndex < 0 || selectedIndex >= values.Count)
        {
            diagnostic = $"Touch Settings row {index} selected index {selectedIndex} is outside {values.Count} values.";
            return false;
        }

        row = new TouchSettingsRowSnapshot(
            index,
            uiType,
            label,
            help,
            values,
            selectedIndex,
            rowAddress);
        diagnostic = string.Empty;
        return true;
    }

    private static bool TryCaptureSpecialControls(
        byte[] rootBytes,
        IReadOnlyList<TouchSettingsSpecialControlObservation> observations,
        out IReadOnlyList<TouchSettingsSpecialControlSnapshot> controls,
        out string diagnostic)
    {
        controls = Array.Empty<TouchSettingsSpecialControlSnapshot>();
        var expected = new Dictionary<int, nuint>
        {
            [1001] = SettingsCaptureMemory.ReadUInt32(rootBytes, Control1001PointerOffset),
            [1002] = SettingsCaptureMemory.ReadUInt32(rootBytes, Control1002PointerOffset),
        };
        var conditional = SettingsCaptureMemory.ReadUInt32(rootBytes, Control1000PointerOffset);
        if (conditional != 0)
        {
            expected.Add(1000, conditional);
        }
        if (expected.Any(pair => !SettingsCaptureMemory.FitsX86Address(pair.Value)))
        {
            diagnostic = "Touch Settings required permanent-control pointer is null or outside x86 memory.";
            return false;
        }
        if (observations.Count != expected.Count ||
            observations.Select(control => control.Key).Distinct().Count() != observations.Count ||
            observations.Select(control => control.ControlAddress).Distinct().Count() != observations.Count)
        {
            diagnostic = "Touch Settings special controls must match the exact unique native permanent-control set.";
            return false;
        }

        var captured = new List<TouchSettingsSpecialControlSnapshot>(observations.Count);
        foreach (var observation in observations)
        {
            if (!expected.TryGetValue(observation.Key, out var expectedAddress) ||
                observation.ControlAddress != expectedAddress ||
                string.IsNullOrWhiteSpace(observation.Label))
            {
                diagnostic = $"Touch Settings special control key {observation.Key} is blank or does not match its live native control pointer.";
                return false;
            }
            captured.Add(new TouchSettingsSpecialControlSnapshot(
                observation.Key,
                observation.ControlAddress,
                observation.Label));
        }

        controls = captured.OrderBy(control => control.Key).ToArray();
        diagnostic = string.Empty;
        return true;
    }
}

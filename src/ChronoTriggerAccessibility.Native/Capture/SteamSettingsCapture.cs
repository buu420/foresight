using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

public static class SteamSettingsCapture
{
    public const uint RootVtableRva = 0x3A702C;
    public const uint InputManagerVtableRva = 0x3A5D0C;
    public const int MaximumCategoryCount = 16;
    public const int MaximumDescriptorCount = 16;
    public const int MaximumRowCount = 64;
    public const int MaximumActiveManagerCount = 64;

    private const int RootSize = 0x340;
    private const int ContextOffset = 0x2F8;
    private const int DescriptorVectorOffset = 0x2FC;
    private const int CategoryVectorOffset = 0x308;
    private const int ModeOffset = 0x314;
    private const int CategoryManagerOffset = 0x324;
    private const int ActiveRootOffset = 0x328;
    private const int ActiveManagerVectorOffset = 0x32C;
    private const int FocusKeyOffset = 0x338;
    private const int ActivePageOffset = 0x33C;
    private const int DescriptorStride = 0x0C;
    private const int CategoryStride = 0x30;
    private const int CategoryLabelOffset = 0x00;
    private const int CategoryHelpOffset = 0x18;
    private const int RowStride = 0x88;
    private const int RowLabelOffset = 0x00;
    private const int RowHelpVectorOffset = 0x18;
    private const int RowValueVectorOffset = 0x24;
    private const int RowSetterOffset = 0x58;
    private const int ManagerKeyOffset = 0x2C4;
    private const int ManagerRequiredSize = ManagerKeyOffset + sizeof(int);

    public static bool TryCreateSnapshot(
        IReadableMemory? memory,
        nuint imageBase,
        nuint root,
        IReadOnlyList<SteamSettingsValueObservation>? valueObservations,
        out SteamSettingsSnapshot snapshot,
        out string diagnostic)
    {
        try
        {
            return TryCreateSnapshotCore(
                memory,
                imageBase,
                root,
                valueObservations,
                out snapshot,
                out diagnostic);
        }
        catch (Exception exception)
        {
            snapshot = null!;
            diagnostic = $"Steam Settings capture failed safely: {exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    private static bool TryCreateSnapshotCore(
        IReadableMemory? memory,
        nuint imageBase,
        nuint root,
        IReadOnlyList<SteamSettingsValueObservation>? valueObservations,
        out SteamSettingsSnapshot snapshot,
        out string diagnostic)
    {
        snapshot = null!;
        if (memory is null || !SettingsCaptureMemory.FitsX86Range(root, RootSize))
        {
            diagnostic = "Steam Settings memory or root is unavailable or outside the x86 address space.";
            return false;
        }
        if (valueObservations is null || valueObservations.Any(observation => observation is null))
        {
            diagnostic = "Steam Settings requires a non-null audited builder-observation collection.";
            return false;
        }
        if (!SettingsCaptureMemory.TryResolveVtable(imageBase, RootVtableRva, out var expectedRootVtable, out diagnostic) ||
            !SettingsCaptureMemory.TryResolveVtable(imageBase, InputManagerVtableRva, out var expectedManagerVtable, out diagnostic))
        {
            return false;
        }
        if (!SettingsCaptureMemory.TryReadBytes(memory, root, RootSize, "Steam Settings root", out var rootBytes, out diagnostic))
        {
            return false;
        }
        if (SettingsCaptureMemory.ReadUInt32(rootBytes, 0) != expectedRootVtable)
        {
            diagnostic = "Steam Settings root vtable does not match MenuNodeConfigSteam.";
            return false;
        }

        var contextValue = SettingsCaptureMemory.ReadInt32(rootBytes, ContextOffset);
        if (contextValue is not 0 and not 1)
        {
            diagnostic = $"Steam Settings context {contextValue} is outside the audited in-game/title values 0 and 1.";
            return false;
        }
        var context = (SteamSettingsContext)contextValue;
        var modeValue = rootBytes[ModeOffset];
        if (modeValue is not 0 and not 1)
        {
            diagnostic = $"Steam Settings mode byte {modeValue} is outside the audited page/category values 0 and 1.";
            return false;
        }
        var mode = (SteamSettingsMode)modeValue;

        if (!SettingsCaptureMemory.TryReadVector(
                rootBytes.AsSpan(DescriptorVectorOffset, SettingsCaptureMemory.VectorHeaderSize),
                DescriptorStride,
                MaximumDescriptorCount,
                allowEmpty: false,
                "Steam Settings descriptor",
                out var descriptorVector,
                out diagnostic) ||
            !SettingsCaptureMemory.TryReadVector(
                rootBytes.AsSpan(CategoryVectorOffset, SettingsCaptureMemory.VectorHeaderSize),
                CategoryStride,
                MaximumCategoryCount,
                allowEmpty: false,
                "Steam Settings category",
                out var categoryVector,
                out diagnostic) ||
            !SettingsCaptureMemory.TryReadVector(
                rootBytes.AsSpan(ActiveManagerVectorOffset, SettingsCaptureMemory.VectorHeaderSize),
                sizeof(uint),
                MaximumActiveManagerCount,
                allowEmpty: true,
                "Steam Settings active-manager",
                out var managerVector,
                out diagnostic))
        {
            return false;
        }

        var expectedCategoryCount = context == SteamSettingsContext.InGame ? 6 : 4;
        var expectedDescriptorCount = context == SteamSettingsContext.InGame ? 4 : 2;
        if (categoryVector.Count != expectedCategoryCount || descriptorVector.Count != expectedDescriptorCount)
        {
            diagnostic = $"Steam Settings {context} requires {expectedCategoryCount} categories and {expectedDescriptorCount} ordinary descriptors; observed {categoryVector.Count} and {descriptorVector.Count}.";
            return false;
        }
        if (!SettingsCaptureMemory.TryReadBytes(
                memory,
                descriptorVector.Begin,
                descriptorVector.ByteLength,
                "Steam Settings descriptors",
                out var descriptorBytes,
                out diagnostic) ||
            !SettingsCaptureMemory.TryReadBytes(
                memory,
                categoryVector.Begin,
                categoryVector.ByteLength,
                "Steam Settings categories",
                out var categoryBytes,
                out diagnostic))
        {
            return false;
        }

        var guards = new List<SettingsMemoryGuard>
        {
            new(root, rootBytes, "Steam Settings root"),
            new(descriptorVector.Begin, descriptorBytes, "Steam Settings descriptors"),
            new(categoryVector.Begin, categoryBytes, "Steam Settings categories"),
        };
        var categories = new SteamSettingsCategorySnapshot[categoryVector.Count];
        for (var index = 0; index < categories.Length; index++)
        {
            if (!SettingsCaptureMemory.TryElementAddress(categoryVector.Begin, index, CategoryStride, out var categoryAddress) ||
                !SettingsCaptureMemory.TryAddX86(categoryAddress, CategoryHelpOffset, out var helpAddress))
            {
                diagnostic = $"Steam Settings category {index} address crosses x86 memory.";
                return false;
            }
            var offset = index * CategoryStride;
            if (!SettingsCaptureMemory.TryReadString(
                    memory,
                    categoryAddress,
                    categoryBytes.AsSpan(offset + CategoryLabelOffset, MsvcStringReader.LayoutSize),
                    $"Steam Settings category {index} label",
                    out var label,
                    out diagnostic) ||
                !SettingsCaptureMemory.TryReadString(
                    memory,
                    helpAddress,
                    categoryBytes.AsSpan(offset + CategoryHelpOffset, MsvcStringReader.LayoutSize),
                    $"Steam Settings category {index} help",
                    out var help,
                    out diagnostic))
            {
                return false;
            }

            categories[index] = new SteamSettingsCategorySnapshot(
                index,
                index < descriptorVector.Count
                    ? SteamSettingsCategoryKind.OrdinaryPage
                    : SteamSettingsCategoryKind.SpecialAction,
                label,
                help);
        }

        var categoryManagerAddress = SettingsCaptureMemory.ReadUInt32(rootBytes, CategoryManagerOffset);
        if (!TryCaptureManager(
                memory,
                categoryManagerAddress,
                expectedManagerVtable,
                "Steam Settings category manager",
                guards,
                out var categoryManagerKey,
                out diagnostic))
        {
            return false;
        }

        byte[] managerPointerBytes = [];
        var activeManagerAddresses = new nuint[managerVector.Count];
        var activeManagerKeys = new int[managerVector.Count];
        if (managerVector.Count != 0)
        {
            if (!SettingsCaptureMemory.TryReadBytes(
                    memory,
                    managerVector.Begin,
                    managerVector.ByteLength,
                    "Steam Settings active-manager pointers",
                    out managerPointerBytes,
                    out diagnostic))
            {
                return false;
            }
            guards.Add(new SettingsMemoryGuard(
                managerVector.Begin,
                managerPointerBytes,
                "Steam Settings active-manager pointers"));
            for (var index = 0; index < activeManagerAddresses.Length; index++)
            {
                var managerAddress = BinaryPrimitives.ReadUInt32LittleEndian(managerPointerBytes.AsSpan(index * sizeof(uint)));
                if (!TryCaptureManager(
                        memory,
                        managerAddress,
                        expectedManagerVtable,
                        $"Steam Settings active manager {index}",
                        guards,
                        out var managerKey,
                        out diagnostic))
                {
                    return false;
                }
                activeManagerAddresses[index] = managerAddress;
                activeManagerKeys[index] = managerKey;
            }
            if (activeManagerAddresses.Distinct().Count() != activeManagerAddresses.Length)
            {
                diagnostic = "Steam Settings active-manager vector contains duplicate manager pointers.";
                return false;
            }
        }

        var activePage = SettingsCaptureMemory.ReadInt32(rootBytes, ActivePageOffset);
        if (activePage < 0 || activePage >= categoryVector.Count)
        {
            diagnostic = $"Steam Settings active category/page {activePage} is outside {categoryVector.Count} categories.";
            return false;
        }

        SteamSettingsPageSnapshot? page = null;
        SteamSettingsFocusKind focusKind;
        int focusKey;
        int? focusedCategory;
        int? focusedRow;
        int? focusedSubcontrol;
        if (mode == SteamSettingsMode.Categories)
        {
            if (valueObservations.Count != 0)
            {
                diagnostic = "Steam Settings category mode cannot consume stale page value observations.";
                return false;
            }
            if (categoryManagerKey < 0 || categoryManagerKey >= categoryVector.Count)
            {
                diagnostic = $"Steam Settings category-manager key {categoryManagerKey} is outside the category list.";
                return false;
            }

            focusKind = SteamSettingsFocusKind.Category;
            focusKey = categoryManagerKey;
            focusedCategory = categoryManagerKey;
            focusedRow = null;
            focusedSubcontrol = null;
        }
        else
        {
            if (activePage >= descriptorVector.Count)
            {
                diagnostic = $"Steam Settings page mode active page {activePage} does not identify an ordinary descriptor.";
                return false;
            }
            if (categoryManagerKey != activePage)
            {
                diagnostic = $"Steam Settings category-manager key {categoryManagerKey} does not match active page {activePage}.";
                return false;
            }
            var activeRoot = SettingsCaptureMemory.ReadUInt32(rootBytes, ActiveRootOffset);
            if (!SettingsCaptureMemory.FitsX86Address(activeRoot))
            {
                diagnostic = "Steam Settings active page root is null or outside x86 memory.";
                return false;
            }
            if (managerVector.Count == 0)
            {
                diagnostic = "Steam Settings page mode requires at least one captured active-page input manager.";
                return false;
            }

            var descriptorOffset = activePage * DescriptorStride;
            if (!SettingsCaptureMemory.TryElementAddress(
                    descriptorVector.Begin,
                    activePage,
                    DescriptorStride,
                    out var activeDescriptorAddress) ||
                !SettingsCaptureMemory.TryReadVector(
                    descriptorBytes.AsSpan(descriptorOffset, DescriptorStride),
                    RowStride,
                    MaximumRowCount,
                    allowEmpty: false,
                    "Steam Settings active row",
                    out var rowVector,
                    out diagnostic) ||
                !SettingsCaptureMemory.TryReadBytes(
                    memory,
                    rowVector.Begin,
                    rowVector.ByteLength,
                    "Steam Settings active rows",
                    out var rowBytes,
                    out diagnostic))
            {
                return false;
            }
            guards.Add(new SettingsMemoryGuard(activeDescriptorAddress, descriptorBytes.AsSpan(descriptorOffset, DescriptorStride).ToArray(), "Steam Settings active descriptor"));
            guards.Add(new SettingsMemoryGuard(rowVector.Begin, rowBytes, "Steam Settings active rows"));

            var rows = new SteamSettingsRowSnapshot[rowVector.Count];
            var consumedObservations = new HashSet<SteamSettingsValueObservation>();
            for (var index = 0; index < rows.Length; index++)
            {
                if (!TryCaptureRow(
                        memory,
                        root,
                        activePage,
                        rowVector,
                        rowBytes,
                        index,
                        valueObservations,
                        consumedObservations,
                        guards,
                        out rows[index],
                        out diagnostic))
                {
                    return false;
                }
            }
            if (consumedObservations.Count != valueObservations.Count)
            {
                diagnostic = "Steam Settings received a value observation that does not identify one live active-page value row.";
                return false;
            }

            focusKey = SettingsCaptureMemory.ReadInt32(rootBytes, FocusKeyOffset);
            if (focusKey < 0 || activeManagerKeys.Count(key => key == focusKey) != 1)
            {
                diagnostic = $"Steam Settings focus key {focusKey} does not match exactly one active-page manager.";
                return false;
            }
            var rowIndex = focusKey / 10;
            var subcontrol = focusKey % 10;
            if (rowIndex < rows.Length)
            {
                focusKind = SteamSettingsFocusKind.Row;
                focusedRow = rowIndex;
                focusedSubcontrol = subcontrol;
            }
            else if (rowIndex == rows.Length && subcontrol == 0)
            {
                focusKind = SteamSettingsFocusKind.ReturnToCategories;
                focusedRow = null;
                focusedSubcontrol = null;
            }
            else
            {
                diagnostic = $"Steam Settings focus key {focusKey} does not identify a live row or the return control.";
                return false;
            }
            focusedCategory = null;

            var category = categories[activePage];
            var returnControl = new MenuControlSnapshot(
                new string(category.Label.AsSpan()),
                null,
                new string(category.Help.AsSpan()),
                checked(rows.Length * 10),
                rows.Length + 1,
                rows.Length + 1,
                Enabled: true,
                Visible: true);
            page = new SteamSettingsPageSnapshot(activePage, activeRoot, rows, returnControl);
        }

        if (!SettingsCaptureMemory.TryRevalidate(memory, guards, out diagnostic))
        {
            return false;
        }

        snapshot = new SteamSettingsSnapshot(
            context,
            mode,
            categories,
            descriptorVector.Count,
            activePage,
            categoryManagerAddress,
            activeManagerAddresses,
            focusKey,
            focusKind,
            focusedCategory,
            focusedRow,
            focusedSubcontrol,
            page);
        diagnostic = string.Empty;
        return true;
    }

    private static bool TryCaptureRow(
        IReadableMemory memory,
        nuint root,
        int activePage,
        SettingsVector rowVector,
        byte[] rowBytes,
        int index,
        IReadOnlyList<SteamSettingsValueObservation> observations,
        ISet<SteamSettingsValueObservation> consumedObservations,
        ICollection<SettingsMemoryGuard> guards,
        out SteamSettingsRowSnapshot row,
        out string diagnostic)
    {
        row = null!;
        if (!SettingsCaptureMemory.TryElementAddress(rowVector.Begin, index, RowStride, out var rowAddress) ||
            !SettingsCaptureMemory.TryAddX86(rowAddress, RowHelpVectorOffset, out var helpHeaderAddress) ||
            !SettingsCaptureMemory.TryAddX86(rowAddress, RowValueVectorOffset, out var valueHeaderAddress))
        {
            diagnostic = $"Steam Settings row {index} address crosses x86 memory.";
            return false;
        }

        var offset = index * RowStride;
        if (!SettingsCaptureMemory.TryReadString(
                memory,
                rowAddress,
                rowBytes.AsSpan(offset + RowLabelOffset, MsvcStringReader.LayoutSize),
                $"Steam Settings row {index} label",
                out var label,
                out diagnostic) ||
            !SettingsCaptureMemory.TryReadStringVector(
                memory,
                helpHeaderAddress,
                rowBytes.AsSpan(offset + RowHelpVectorOffset, SettingsCaptureMemory.VectorHeaderSize),
                allowEmpty: false,
                $"Steam Settings row {index} help",
                guards,
                out var help,
                out _,
                out diagnostic) ||
            !SettingsCaptureMemory.TryReadStringVector(
                memory,
                valueHeaderAddress,
                rowBytes.AsSpan(offset + RowValueVectorOffset, SettingsCaptureMemory.VectorHeaderSize),
                allowEmpty: true,
                $"Steam Settings row {index} values",
                guards,
                out var values,
                out var valueVector,
                out diagnostic))
        {
            return false;
        }

        var matching = observations.Where(observation =>
            observation.RootAddress == root &&
            observation.PageIndex == activePage &&
            observation.RowAddress == rowAddress).ToArray();
        if (values.Count == 0)
        {
            if (matching.Length != 0 || help.Count != 1)
            {
                diagnostic = $"Steam Settings action row {index} requires one help string and no selected-value observation.";
                return false;
            }

            row = new SteamSettingsRowSnapshot(
                index,
                SteamSettingsRowKind.Action,
                label,
                help,
                values,
                null,
                null,
                rowAddress,
                null,
                null);
            return true;
        }
        if (help.Count != 1 && help.Count != values.Count)
        {
            diagnostic = $"Steam Settings value row {index} help count {help.Count} is neither one general string nor {values.Count} index-correlated strings.";
            return false;
        }
        if (matching.Length != 1)
        {
            diagnostic = $"Steam Settings value row {index} requires exactly one audited selected-value source observation.";
            return false;
        }

        var observation = matching[0];
        if (!SettingsCaptureMemory.FitsX86Address(observation.ValueSourceAddress) ||
            observation.ValueSourceAddress < valueVector.Begin)
        {
            diagnostic = $"Steam Settings row {index} selected-value source is outside its live copied value vector.";
            return false;
        }
        var delta = (ulong)observation.ValueSourceAddress - valueVector.Begin;
        if (delta % MsvcStringReader.LayoutSize != 0 ||
            delta / MsvcStringReader.LayoutSize >= (ulong)values.Count)
        {
            diagnostic = $"Steam Settings row {index} selected-value source is not aligned within its live copied 0x18-byte value vector.";
            return false;
        }

        var selectedIndex = checked((int)(delta / MsvcStringReader.LayoutSize));
        if (!SettingsCaptureMemory.TryAddX86(rowAddress, RowSetterOffset, out var setterAddress))
        {
            diagnostic = $"Steam Settings row {index} setter-object address crosses x86 memory.";
            return false;
        }
        consumedObservations.Add(observation);
        row = new SteamSettingsRowSnapshot(
            index,
            SteamSettingsRowKind.Value,
            label,
            help,
            values,
            selectedIndex,
            values[selectedIndex],
            rowAddress,
            setterAddress,
            observation.ValueSourceAddress);
        diagnostic = string.Empty;
        return true;
    }

    private static bool TryCaptureManager(
        IReadableMemory memory,
        nuint manager,
        uint expectedVtable,
        string name,
        ICollection<SettingsMemoryGuard> guards,
        out int key,
        out string diagnostic)
    {
        key = 0;
        if (!SettingsCaptureMemory.FitsX86Range(manager, ManagerRequiredSize))
        {
            diagnostic = $"{name} is null or crosses the x86 address space.";
            return false;
        }
        if (!SettingsCaptureMemory.TryReadBytes(memory, manager, sizeof(uint), $"{name} vtable", out var vtableBytes, out diagnostic))
        {
            return false;
        }
        if (BinaryPrimitives.ReadUInt32LittleEndian(vtableBytes) != expectedVtable)
        {
            diagnostic = $"{name} vtable does not match the audited generic input-manager type.";
            return false;
        }
        if (!SettingsCaptureMemory.TryAddX86(manager, ManagerKeyOffset, out var keyAddress))
        {
            diagnostic = $"{name} key address crosses the x86 address space.";
            return false;
        }
        if (!SettingsCaptureMemory.TryReadBytes(memory, keyAddress, sizeof(int), $"{name} key", out var keyBytes, out diagnostic))
        {
            return false;
        }

        key = BinaryPrimitives.ReadInt32LittleEndian(keyBytes);
        guards.Add(new SettingsMemoryGuard(manager, vtableBytes, $"{name} vtable"));
        guards.Add(new SettingsMemoryGuard(keyAddress, keyBytes, $"{name} key"));
        diagnostic = string.Empty;
        return true;
    }
}

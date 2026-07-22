namespace ChronoTriggerAccessibility.Native.Capture;

public enum BuilderTextPart
{
    Label,
    Value,
    Help,
}

public enum BuilderRenderSource
{
    Value,
    Help,
}

public sealed class BuilderCaptureScope : IDisposable
{
    private static readonly ThreadLocal<BuilderCaptureScope?> Current = new();

    private readonly int owningThreadId = Environment.CurrentManagedThreadId;
    private readonly Dictionary<(nuint Control, BuilderTextPart Part), string> textByControlPart = [];
    private readonly Dictionary<nuint, ConstructedControl> controls = [];
    private readonly Dictionary<nuint, Binding> bindingByControl = [];
    private readonly HashSet<int> boundKeys = [];
    private readonly HashSet<int> positions = [];
    private readonly List<string> errors = [];
    private Focus? focus;
    private nuint? bindingManager;
    private bool disposed;
    private int abandoned;

    public BuilderCaptureScope()
    {
        if (Current.Value is { } existing)
        {
            if (!existing.IsAbandoned)
            {
                throw new InvalidOperationException("A builder capture scope is already active on this thread.");
            }

            Current.Value = null;
        }

        Current.Value = this;
    }

    public bool TryRecordLocalizedText(nuint control, BuilderTextPart part, string? text, out string diagnostic)
    {
        if (!TryUse(out diagnostic))
        {
            return false;
        }
        if (control == 0 || !Enum.IsDefined(part) || string.IsNullOrWhiteSpace(text))
        {
            diagnostic = "A localized text observation has a null control, unsupported text part, or blank text.";
            errors.Add(diagnostic);
            return false;
        }

        return TryStoreText(control, part, text, out diagnostic);
    }

    public bool TryRecordConstructedControl(nuint control, int position, bool enabled, bool visible, out string diagnostic)
    {
        if (!TryUse(out diagnostic))
        {
            return false;
        }
        if (control == 0 || position < 0)
        {
            diagnostic = "A constructed control has a null pointer or negative position.";
            errors.Add(diagnostic);
            return false;
        }
        if (controls.ContainsKey(control) || !positions.Add(position))
        {
            diagnostic = "A constructed control or position was observed more than once.";
            errors.Add(diagnostic);
            return false;
        }

        controls.Add(control, new ConstructedControl(position, enabled, visible));
        diagnostic = string.Empty;
        return true;
    }

    public bool TryRecordManagerKeyBinding(nuint manager, nuint control, int key, out string diagnostic)
    {
        if (!TryUse(out diagnostic))
        {
            return false;
        }
        if (manager == 0 || control == 0 || key < 0)
        {
            diagnostic = "A manager/key binding has a null pointer or negative key.";
            errors.Add(diagnostic);
            return false;
        }
        if (bindingByControl.ContainsKey(control))
        {
            diagnostic = "A control was bound more than once.";
            errors.Add(diagnostic);
            return false;
        }
        if (bindingManager is not null && bindingManager != manager)
        {
            diagnostic = "Multiple managers cannot be correlated into one menu snapshot.";
            errors.Add(diagnostic);
            return false;
        }
        if (!boundKeys.Add(key))
        {
            diagnostic = "A duplicate integer manager key was observed.";
            errors.Add(diagnostic);
            return false;
        }

        bindingManager ??= manager;
        bindingByControl.Add(control, new Binding(manager, key));
        diagnostic = string.Empty;
        return true;
    }

    public bool TryRecordFocus(nuint manager, int key, out string diagnostic)
    {
        if (!TryUse(out diagnostic))
        {
            return false;
        }
        if (manager == 0 || key < 0 || focus is not null)
        {
            diagnostic = "A focus observation has a null manager, negative key, or is ambiguous.";
            errors.Add(diagnostic);
            return false;
        }

        focus = new Focus(manager, key);
        diagnostic = string.Empty;
        return true;
    }

    public bool TryRecordRenderedString(nuint control, BuilderRenderSource source, string? text, out string diagnostic)
    {
        if (!TryUse(out diagnostic))
        {
            return false;
        }
        if (control == 0 || string.IsNullOrWhiteSpace(text) || source is not BuilderRenderSource.Value and not BuilderRenderSource.Help)
        {
            diagnostic = "A rendered string has a null control, unsupported render source, or blank text.";
            errors.Add(diagnostic);
            return false;
        }

        var part = source == BuilderRenderSource.Value ? BuilderTextPart.Value : BuilderTextPart.Help;
        return TryStoreText(control, part, text, out diagnostic);
    }

    public bool TryCreateSnapshot(string? status, out MenuCaptureResult result)
    {
        if (!TryUse(out var diagnostic))
        {
            result = MenuCaptureResult.Failure(diagnostic);
            return false;
        }
        if (errors.Count > 0)
        {
            result = MenuCaptureResult.Failure(string.Join(" ", errors));
            return false;
        }
        var controlPointers = controls.Keys.ToHashSet();
        if (controlPointers.Count == 0 || !controlPointers.SetEquals(bindingByControl.Keys))
        {
            result = MenuCaptureResult.Failure("Constructed controls and manager bindings do not have the same pointer set.");
            return false;
        }
        if (textByControlPart.Keys.Any(observation => !controlPointers.Contains(observation.Control)))
        {
            result = MenuCaptureResult.Failure("A localized or rendered observation belongs to an unknown constructed control.");
            return false;
        }
        if (focus is not null && (bindingManager != focus.Manager || !boundKeys.Contains(focus.Key)))
        {
            result = MenuCaptureResult.Failure("The captured focus does not match an observed manager/key binding.");
            return false;
        }

        var ordered = new List<MenuControlSnapshot>(controls.Count);
        foreach (var (control, constructed) in controls.OrderBy(pair => pair.Value.Position))
        {
            if (!bindingByControl.TryGetValue(control, out var binding) ||
                !textByControlPart.TryGetValue((control, BuilderTextPart.Label), out var label))
            {
                result = MenuCaptureResult.Failure("A constructed control has no unambiguous binding or localized label.");
                return false;
            }

            ordered.Add(new MenuControlSnapshot(
                label,
                TryGetText(control, BuilderTextPart.Value),
                TryGetText(control, BuilderTextPart.Help),
                binding.Key,
                constructed.Position + 1,
                controls.Count,
                constructed.Enabled,
                constructed.Visible));
        }
        result = MenuCaptureResult.Success(new MenuStatusSnapshot(status, ordered, focus?.Key));
        return true;
    }

    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId != owningThreadId)
        {
            Interlocked.Exchange(ref abandoned, 1);
            throw new InvalidOperationException("Builder capture scope disposal was attempted from a different thread.");
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

    private bool TryStoreText(nuint control, BuilderTextPart part, string text, out string diagnostic)
    {
        if (!textByControlPart.TryAdd((control, part), new string(text.AsSpan())))
        {
            diagnostic = "An ambiguous correlation supplied more than one text observation for the same control and text part.";
            errors.Add(diagnostic);
            return false;
        }

        diagnostic = string.Empty;
        return true;
    }

    private string? TryGetText(nuint control, BuilderTextPart part) =>
        textByControlPart.TryGetValue((control, part), out var text) ? text : null;

    private bool IsAbandoned => Volatile.Read(ref abandoned) != 0;

    private bool TryUse(out string diagnostic)
    {
        if (Environment.CurrentManagedThreadId != owningThreadId)
        {
            diagnostic = "Builder capture scope access was attempted from a different thread.";
            return false;
        }
        if (disposed)
        {
            diagnostic = "Builder capture scope has been disposed.";
            return false;
        }
        if (IsAbandoned)
        {
            diagnostic = "Builder capture scope was abandoned after a cross-thread disposal attempt.";
            return false;
        }

        diagnostic = string.Empty;
        return true;
    }

    private sealed record ConstructedControl(int Position, bool Enabled, bool Visible);
    private sealed record Binding(nuint Manager, int Key);
    private sealed record Focus(nuint Manager, int Key);
}

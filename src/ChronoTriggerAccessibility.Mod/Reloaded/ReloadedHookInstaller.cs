using System.Collections.ObjectModel;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Runtime;
using Reloaded.Hooks.Definitions;

namespace ChronoTriggerAccessibility.Mod.Reloaded;

public interface IHookRegistration
{
    string Name { get; }
    IPreparedHook Prepare(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary);
}

public interface IPreparedHook
{
    string Name { get; }
    bool IsActive { get; }
    IReadOnlyCollection<object> LifetimeRoots { get; }
    void Activate();
    void Disable();
}

public interface IHookActivationObserver
{
    void AfterHooksActivated();
    void AfterHooksDisabled();
}

public sealed class HookRollbackException : InvalidOperationException
{
    public HookRollbackException(IEnumerable<Exception> failures, bool hooksConfirmedInactive)
        : this(failures.ToArray(), hooksConfirmedInactive)
    {
    }

    private HookRollbackException(Exception[] failures, bool hooksConfirmedInactive)
        : base(
            "Hook rollback integrity failure: " + string.Join(" | ", failures.Select(failure => failure.Message)),
            new AggregateException(failures))
    {
        Failures = new ReadOnlyCollection<Exception>(failures);
        HooksConfirmedInactive = hooksConfirmedInactive;
    }

    public IReadOnlyList<Exception> Failures { get; }
    public bool HooksConfirmedInactive { get; }
}

public sealed class HookTransactionException(
    string phase,
    Exception originalFailure,
    Exception rollbackFailure)
    : InvalidOperationException(
        $"{phase} failed: {originalFailure.Message} Rollback integrity failure: {rollbackFailure.Message}",
        new AggregateException(originalFailure, rollbackFailure))
{
    public Exception OriginalFailure { get; } = originalFailure;
    public Exception RollbackFailure { get; } = rollbackFailure;
}

public sealed class ReloadedPreparedHook<TDelegate> : IPreparedHook
    where TDelegate : Delegate
{
    private readonly IHook<TDelegate> hook;
    private readonly TDelegate detourRoot;

    public ReloadedPreparedHook(string name, IHook<TDelegate> hook, TDelegate detourRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        this.hook = hook ?? throw new ArgumentNullException(nameof(hook));
        this.detourRoot = detourRoot ?? throw new ArgumentNullException(nameof(detourRoot));
        LifetimeRoots = [this.detourRoot];
    }

    public string Name { get; }
    public bool IsActive => hook.IsHookEnabled;
    public IReadOnlyCollection<object> LifetimeRoots { get; }
    public TDelegate OriginalFunction => hook.OriginalFunction;

    public void Activate() => hook.Activate();

    public void Disable()
    {
        if (hook.IsHookEnabled)
        {
            hook.Disable();
        }
    }
}

public sealed class ReloadedHookInstaller : IRuntimeHookInstaller
{
    private readonly IReadOnlyList<IHookRegistration> registrations;
    private readonly IReadOnlyList<IHookActivationObserver> activationObservers;
    private readonly List<IPreparedHook> preparedHooks = [];
    private readonly List<object> lifetimeRoots = [];
    private bool prepared;

    public ReloadedHookInstaller(
        IEnumerable<IHookRegistration> registrations,
        IEnumerable<IHookActivationObserver>? activationObservers = null)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        this.registrations = new ReadOnlyCollection<IHookRegistration>(registrations.ToArray());
        this.activationObservers = new ReadOnlyCollection<IHookActivationObserver>(
            (activationObservers ?? Array.Empty<IHookActivationObserver>()).ToArray());
    }

    public IReadOnlyList<IPreparedHook> PreparedHooks => preparedHooks.AsReadOnly();

    public int LifetimeRootCount => lifetimeRoots.Count;

    public void PrepareAll(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(boundary);
        if (prepared)
        {
            throw new InvalidOperationException("Hooks may only be prepared once.");
        }

        try
        {
            foreach (var registration in registrations)
            {
                var hook = registration.Prepare(build, boundary)
                    ?? throw new InvalidOperationException($"Hook registration '{registration.Name}' returned null.");

                // Root and track the hook before inspecting it. If any property or cleanup operation
                // throws, rollback must still retain and revisit this exact native detour.
                preparedHooks.Add(hook);
                lifetimeRoots.Add(hook);
                lifetimeRoots.AddRange(hook.LifetimeRoots);

                if (hook.IsActive)
                {
                    throw new InvalidOperationException(
                        $"Hook registration '{registration.Name}' was unexpectedly active during preparation.");
                }
            }

            prepared = true;
        }
        catch (Exception originalFailure)
        {
            try
            {
                DisableAll();
            }
            catch (Exception rollbackFailure)
            {
                throw new HookTransactionException("Hook preparation", originalFailure, rollbackFailure);
            }

            throw;
        }
    }

    public void ActivateAll()
    {
        if (!prepared)
        {
            throw new InvalidOperationException("All hooks must be prepared before activation.");
        }

        try
        {
            foreach (var hook in preparedHooks)
            {
                hook.Activate();
            }

            foreach (var observer in activationObservers)
            {
                observer.AfterHooksActivated();
            }
        }
        catch (Exception originalFailure)
        {
            try
            {
                DisableAll();
            }
            catch (Exception rollbackFailure)
            {
                throw new HookTransactionException("Hook activation", originalFailure, rollbackFailure);
            }

            throw;
        }
    }

    public void DisableAll()
    {
        var failures = new List<Exception>();
        for (var index = preparedHooks.Count - 1; index >= 0; index--)
        {
            var hook = preparedHooks[index];
            try
            {
                hook.Disable();
            }
            catch (Exception exception)
            {
                failures.Add(new InvalidOperationException(
                    $"Disable failed for hook '{hook.Name}': {exception.Message}", exception));
            }
        }

        for (var index = activationObservers.Count - 1; index >= 0; index--)
        {
            try
            {
                activationObservers[index].AfterHooksDisabled();
            }
            catch (Exception exception)
            {
                failures.Add(new InvalidOperationException(
                    $"Post-disable cleanup failed for activation observer {index}: {exception.Message}",
                    exception));
            }
        }

        var hooksConfirmedInactive = true;
        foreach (var hook in preparedHooks)
        {
            try
            {
                if (hook.IsActive)
                {
                    hooksConfirmedInactive = false;
                    failures.Add(new InvalidOperationException(
                        $"Hook '{hook.Name}' remained active after rollback."));
                }
            }
            catch (Exception exception)
            {
                hooksConfirmedInactive = false;
                failures.Add(new InvalidOperationException(
                    $"Could not verify inactive state for hook '{hook.Name}': {exception.Message}", exception));
            }
        }

        if (failures.Count > 0)
        {
            throw new HookRollbackException(failures, hooksConfirmedInactive);
        }
    }
}

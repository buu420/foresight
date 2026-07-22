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
    private readonly List<IPreparedHook> preparedHooks = [];
    private readonly List<object> lifetimeRoots = [];
    private bool prepared;

    public ReloadedHookInstaller(IEnumerable<IHookRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        this.registrations = new ReadOnlyCollection<IHookRegistration>(registrations.ToArray());
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
                if (hook.IsActive)
                {
                    hook.Disable();
                    throw new InvalidOperationException(
                        $"Hook registration '{registration.Name}' activated during preparation.");
                }

                preparedHooks.Add(hook);
                lifetimeRoots.Add(hook);
                lifetimeRoots.AddRange(hook.LifetimeRoots);
            }

            prepared = true;
        }
        catch
        {
            DisableAll();
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
        }
        catch
        {
            DisableAll();
            throw;
        }
    }

    public void DisableAll()
    {
        for (var index = preparedHooks.Count - 1; index >= 0; index--)
        {
            try
            {
                preparedHooks[index].Disable();
            }
            catch (Exception)
            {
                // Continue rollback: every remaining hook must receive a disable attempt.
            }
        }
    }
}

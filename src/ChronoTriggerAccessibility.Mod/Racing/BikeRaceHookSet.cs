using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Hooks;

namespace ChronoTriggerAccessibility.Mod.Racing;

public sealed class BikeRaceHookSet(IRuntimeNativeHookFactory factory, Action<nuint> update,
    Action<nuint> close, Action<nuint> bindImage, Action disable) : IHookActivationObserver
{
    private bool active;
    private int prepared;
    private IReadOnlyList<IHookRegistration>? registrations;
    public IReadOnlyList<IHookRegistration> Registrations => registrations ??=
        Array.AsReadOnly<IHookRegistration>([new Registration(this, HookId.BikeRaceUpdate), new Registration(this, HookId.BikeRaceDestructor)]);
    public void AfterHooksActivated()
    {
        if (prepared != 2) throw new InvalidOperationException("Both race boundaries must be prepared.");
        active = true;
    }
    public void AfterHooksDisabled() { active = false; disable(); }

    private sealed class Registration(BikeRaceHookSet owner, HookId id) : IHookRegistration
    {
        private bool prepared;
        public string Name => GameVersionCatalog.Get(id).Symbol;
        public IPreparedHook Prepare(IVerifiedGameBuild build, UnmanagedBoundaryGuard guard)
        {
            var contract = GameVersionCatalog.Get(id);
            if (prepared || build.ImageBaseAddress == 0 || !build.HookAddresses.TryGetValue(id, out var address) ||
                address != checked(build.ImageBaseAddress + contract.Rva))
                throw new InvalidOperationException("Race hooks require a verified executable and one preparation.");
            owner.Bind(build.ImageBaseAddress);
            var hook = id == HookId.BikeRaceUpdate ? Update(address, guard) : Destroy(address, guard);
            prepared = true; owner.prepared++; return hook;
        }
        private IPreparedHook Update(nuint address, UnmanagedBoundaryGuard guard)
        {
            BikeRaceUpdateDelegate? original = null;
            BikeRaceUpdateDelegate detour = scene =>
            {
                var result = guard.Run(Name + " original", () => original!(scene), (byte)0);
                if (owner.active && !guard.IsFaulted)
                    guard.Run(Name + " capture", () => owner.Observe((nuint)scene, result));
                return result;
            };
            var hook = owner.Create(id, detour, address);
            original = hook.OriginalFunction ?? throw new InvalidOperationException("Race update original is unavailable.");
            return new ReloadedPreparedHook<BikeRaceUpdateDelegate>(Name, hook, detour);
        }
        private IPreparedHook Destroy(nuint address, UnmanagedBoundaryGuard guard)
        {
            BikeRaceDestructorDelegate? original = null;
            BikeRaceDestructorDelegate detour = scene =>
            {
                if (owner.active && !guard.IsFaulted) guard.Run(Name + " leave", () => owner.Close((nuint)scene));
                guard.Run(Name + " original", () => original!(scene));
            };
            var hook = owner.Create(id, detour, address);
            original = hook.OriginalFunction ?? throw new InvalidOperationException("Race destructor original is unavailable.");
            return new ReloadedPreparedHook<BikeRaceDestructorDelegate>(Name, hook, detour);
        }
    }
    private global::Reloaded.Hooks.Definitions.IHook<T> Create<T>(HookId id, T detour, nuint address) where T : Delegate => factory.CreateHook(id, detour, address);
    private void Bind(nuint image) => bindImage(image);
    private void Close(nuint scene) => close(scene);
    private void Observe(nuint scene, byte result) { if (result == 0) update(scene); else close(scene); }
}

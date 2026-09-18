using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Hooks;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Mod.Menus;

/// <summary>Samples the complete shop after its native update and retires it before destruction.
/// Both originals always run; the reader never buys, sells, equips or sends input.</summary>
public sealed class ShopHookSet : IHookActivationObserver
{
    private readonly IRuntimeNativeHookFactory factory;
    private readonly ShopCapture source;
    private readonly FieldSubmenuSession session;
    private readonly Func<long> clock;
    private nuint image;
    private bool active;
    private int prepared;
    private long? lastSample;

    public ShopHookSet(IRuntimeNativeHookFactory factory, IReadableMemory memory, ISemanticEventDispatcher dispatcher,
        Func<bool> foreground, Func<long>? clock = null)
    {
        this.factory = factory;
        this.clock = clock ?? (() => Environment.TickCount64);
        source = new(memory);
        session = new(node => source.Capture(image, node), node => source.IsScene(image, node) ? "Shop" : null,
            _ => false, foreground, dispatcher.Publish, dispatcher.RecordDiagnostic, this.clock);
        Registrations = Array.AsReadOnly<IHookRegistration>([
            new Registration(this, HookId.ShopSceneUpdate), new Registration(this, HookId.ShopSceneDestructor)]);
    }
    public IReadOnlyList<IHookRegistration> Registrations { get; }
    public bool HasContext => session.HasContext;
    public void AfterHooksActivated()
    {
        if (prepared != 2) throw new InvalidOperationException("Both shop boundaries must be prepared.");
        active = true;
    }
    public void AfterHooksDisabled() { active = false; lastSample = null; session.Close(); }

    private sealed class Registration(ShopHookSet owner, HookId id) : IHookRegistration
    {
        private bool prepared;
        public string Name => GameVersionCatalog.Get(id).Symbol;
        public IPreparedHook Prepare(IVerifiedGameBuild build, UnmanagedBoundaryGuard guard)
        {
            var contract = GameVersionCatalog.Get(id);
            if (prepared || build.ImageBaseAddress == 0 || !build.HookAddresses.TryGetValue(id, out var address) ||
                address != checked(build.ImageBaseAddress + contract.Rva))
                throw new InvalidOperationException("Shop hooks require a verified executable and one preparation.");
            owner.image = build.ImageBaseAddress;
            var hook = id == HookId.ShopSceneUpdate ? Update(address, guard) : Destroy(address, guard);
            prepared = true; owner.prepared++; return hook;
        }
        private IPreparedHook Update(nuint address, UnmanagedBoundaryGuard guard)
        {
            ShopSceneUpdateDelegate? original = null;
            ShopSceneUpdateDelegate detour = (scene, delta) =>
            {
                guard.Run(Name + " original", () => original!(scene, delta));
                if (!owner.active || guard.IsFaulted) return;
                guard.Run(Name, () =>
                {
                    var now = owner.clock();
                    if (owner.session.Node == (nuint)scene && owner.lastSample is { } last && now - last < 80) return;
                    owner.lastSample = now;
                    owner.session.Enter((nuint)scene);
                });
            };
            var hook = owner.factory.CreateHook(id, detour, address);
            original = hook.OriginalFunction ?? throw new InvalidOperationException("Shop update original is unavailable.");
            return new ReloadedPreparedHook<ShopSceneUpdateDelegate>(Name, hook, detour);
        }
        private IPreparedHook Destroy(nuint address, UnmanagedBoundaryGuard guard)
        {
            ShopSceneDestructorDelegate? original = null;
            ShopSceneDestructorDelegate detour = (scene, flags) =>
            {
                if (owner.active && !guard.IsFaulted)
                    guard.Run(Name + " leave", () => owner.session.Close((nuint)scene));
                nint result = 0;
                guard.Run(Name + " original", () => result = original!(scene, flags));
                return result;
            };
            var hook = owner.factory.CreateHook(id, detour, address);
            original = hook.OriginalFunction ?? throw new InvalidOperationException("Shop destructor original is unavailable.");
            return new ReloadedPreparedHook<ShopSceneDestructorDelegate>(Name, hook, detour);
        }
    }
}

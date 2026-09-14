using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Hooks;

namespace ChronoTriggerAccessibility.Mod.Battle;

public sealed class BattleHookSet : IHookActivationObserver
{
    private readonly IRuntimeNativeHookFactory factory;
    private readonly Action<nuint> tick, close, message, render, bindImageBase;
    private readonly Action<nuint,int> number, miss;
    private readonly Action disabled;
    private bool active;
    private int prepared;
    private static readonly HookId[] Boundaries = [HookId.BattleHudRefresh, HookId.BattleMenuDestructor,
        HookId.BattleMessageDisplay, HookId.BattleDamageNumber, HookId.BattleMiss, HookId.BattleDamageRender];

    public BattleHookSet(IRuntimeNativeHookFactory factory, Action<nuint> tick, Action<nuint> close,
        Action<nuint> message, Action<nuint,int> number, Action<nuint,int> miss,
        Action<nuint> render, Action<nuint> bindImageBase, Action disabled)
    {
        this.factory = factory; this.tick = tick; this.close = close; this.message = message;
        this.number = number; this.miss = miss; this.render = render; this.bindImageBase = bindImageBase;
        this.disabled = disabled;
        Registrations = Array.AsReadOnly<IHookRegistration>([.. Boundaries.Select(id => new Registration(this, id))]);
    }
    public IReadOnlyList<IHookRegistration> Registrations { get; }
    public void AfterHooksActivated()
    {
        if (prepared != Boundaries.Length) throw new InvalidOperationException("All battle boundaries must be prepared.");
        active = true;
    }
    public void AfterHooksDisabled() { active = false; disabled(); }

    private void Invoke(UnmanagedBoundaryGuard guard, string name, Action original, Action observation, bool before = false)
    {
        // Teardown invalidates managed ownership before native memory is freed.
        // A failed observation must never prevent or repeat the original call.
        if (before && active) guard.Run(name, observation);
        guard.Run(name + " original", original);
        if (!before && active && !guard.IsFaulted) guard.Run(name, observation);
    }

    private sealed class Registration(BattleHookSet owner, HookId id) : IHookRegistration
    {
        private bool prepared;
        public string Name => GameVersionCatalog.Get(id).Symbol;
        public IPreparedHook Prepare(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary)
        {
            var contract = GameVersionCatalog.Get(id);
            if (prepared || build.ImageBaseAddress == 0 || !build.HookAddresses.TryGetValue(id, out var address) ||
                address != checked(build.ImageBaseAddress + contract.Rva))
                throw new InvalidOperationException("Battle hooks require one preparation against the verified executable.");
            owner.bindImageBase(build.ImageBaseAddress);
            IPreparedHook hook = id switch
            {
                HookId.BattleHudRefresh or HookId.BattleMenuDestructor => Member(address, boundary),
                HookId.BattleMessageDisplay or HookId.BattleDamageNumber => Seven(address, boundary),
                HookId.BattleMiss => Miss(address, boundary),
                HookId.BattleDamageRender => Render(address, boundary),
                _ => throw new InvalidOperationException("Unknown battle boundary."),
            };
            prepared = true; owner.prepared++;
            return hook;
        }

        private IPreparedHook Member(nuint address, UnmanagedBoundaryGuard guard)
        {
            BattleMenuMemberDelegate? original = null;
            var destructor = id == HookId.BattleMenuDestructor;
            BattleMenuMemberDelegate detour = menu => owner.Invoke(guard, Name,
                () => original!(menu), () => (destructor ? owner.close : owner.tick)((nuint)menu), destructor);
            var hook = owner.factory.CreateHook(id, detour, address);
            original = hook.OriginalFunction ?? throw new InvalidOperationException("Battle original is unavailable.");
            return new ReloadedPreparedHook<BattleMenuMemberDelegate>(Name, hook, detour);
        }
        private IPreparedHook Seven(nuint address, UnmanagedBoundaryGuard guard)
        {
            BattleSevenWordDelegate? original = null;
            BattleSevenWordDelegate detour = (menu,a1,a2,a3,a4,a5,a6,a7) => owner.Invoke(guard, Name,
                () => original!(menu,a1,a2,a3,a4,a5,a6,a7), () =>
                {
                    if (id == HookId.BattleMessageDisplay) owner.message((nuint)menu);
                    else if ((long)a1 is >= 0x42 and <= 0x4C) owner.number((nuint)menu, (int)a1 - 0x42);
                });
            var hook = owner.factory.CreateHook(id, detour, address);
            original = hook.OriginalFunction ?? throw new InvalidOperationException("Battle original is unavailable.");
            return new ReloadedPreparedHook<BattleSevenWordDelegate>(Name, hook, detour);
        }
        private IPreparedHook Miss(nuint address, UnmanagedBoundaryGuard guard)
        {
            BattleMissDelegate? original = null;
            BattleMissDelegate detour = (menu,a1,a2,a3,a4) => owner.Invoke(guard, Name,
                () => original!(menu,a1,a2,a3,a4), () =>
                {
                    if ((long)a1 is >= 0x42 and <= 0x4C) owner.miss((nuint)menu, (int)a1 - 0x42);
                });
            var hook = owner.factory.CreateHook(id, detour, address);
            original = hook.OriginalFunction ?? throw new InvalidOperationException("Battle original is unavailable.");
            return new ReloadedPreparedHook<BattleMissDelegate>(Name, hook, detour);
        }
        private IPreparedHook Render(nuint address, UnmanagedBoundaryGuard guard)
        {
            BattleRenderDelegate? original = null;
            BattleRenderDelegate detour = (menu,a1,a2) => owner.Invoke(guard, Name,
                () => original!(menu,a1,a2), () => owner.render((nuint)menu));
            var hook = owner.factory.CreateHook(id, detour, address);
            original = hook.OriginalFunction ?? throw new InvalidOperationException("Battle original is unavailable.");
            return new ReloadedPreparedHook<BattleRenderDelegate>(Name, hook, detour);
        }
    }
}

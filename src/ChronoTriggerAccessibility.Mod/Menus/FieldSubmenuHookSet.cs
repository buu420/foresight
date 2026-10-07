using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Hooks;

namespace ChronoTriggerAccessibility.Mod.Menus;

public sealed class FieldSubmenuHookSet : IHookActivationObserver
{
    private readonly IRuntimeNativeHookFactory factory;
    private readonly FieldSubmenuSource source;
    private readonly FieldSubmenuSession session;
    private static readonly HookId[] Boundaries = [HookId.ClassicFieldMenuReplace, HookId.TouchFieldMenuReplace,
        HookId.SaveSlotOpen, HookId.MenuManagerDispatch, HookId.InventoryHelpRefresh, HookId.SaveSlotDetailsRefresh,
        HookId.MenuManagerUpdate, HookId.FormationSceneInit, HookId.FormationSceneDestructor];
    private bool active;
    private int prepared;
    private long lastSample;
    // The End of Time party change is its own scene; its page never reaches the field menu.
    private nuint formationScene, formationPage;
    public FieldSubmenuHookSet(IRuntimeNativeHookFactory factory, FieldSubmenuSource source, FieldSubmenuSession session)
    {
        this.factory = factory; this.source = source; this.session = session;
        Registrations = Array.AsReadOnly<IHookRegistration>([.. Boundaries.Select(id => new Registration(this, id))]);
    }
    public IReadOnlyList<IHookRegistration> Registrations { get; }
    public void AfterHooksActivated()
    {
        if (prepared != Boundaries.Length) throw new InvalidOperationException("All submenu boundaries must be prepared.");
        active = true;
    }
    public void AfterHooksDisabled() { active = false; formationScene = formationPage = 0; session.Close(); }

    private void Observe(UnmanagedBoundaryGuard guard, string name, Action read)
    {
        if (active && !guard.IsFaulted) guard.Run(name, read);
    }
    private sealed class Registration(FieldSubmenuHookSet owner, HookId id) : IHookRegistration
    {
        private bool prepared;
        public string Name => GameVersionCatalog.Get(id).Symbol;
        public IPreparedHook Prepare(IVerifiedGameBuild build, UnmanagedBoundaryGuard guard)
        {
            var contract = GameVersionCatalog.Get(id);
            if (prepared || build.ImageBaseAddress == 0 || !build.HookAddresses.TryGetValue(id, out var address) ||
                address != checked(build.ImageBaseAddress + contract.Rva))
                throw new InvalidOperationException("Submenu hooks require the verified executable and one preparation.");
            owner.source.BindImageBase(build.ImageBaseAddress);
            IPreparedHook hook = id switch
            {
                HookId.SaveSlotOpen => Open(address, guard),
                HookId.MenuManagerDispatch => Dispatch(address, guard),
                HookId.FormationSceneInit => FormationInit(address, guard),
                HookId.FormationSceneDestructor => FormationDestroy(address, guard),
                _ => Member(address, guard),
            };
            prepared = true; owner.prepared++; return hook;
        }
        private IPreparedHook Member(nuint address, UnmanagedBoundaryGuard guard)
        {
            SubmenuNodeWordDelegate? original = null;
            var replace = id is HookId.ClassicFieldMenuReplace or HookId.TouchFieldMenuReplace;
            SubmenuNodeWordDelegate detour = (node, value) =>
            {
                if (replace && owner.session.Node != (nuint)value)
                    owner.Observe(guard, Name + " leave", () => owner.session.Close());
                guard.Run(Name + " original", () => original!(node, value));
                owner.Observe(guard, Name, () =>
                {
                    if (replace)
                    {
                        if (owner.source.IsAttached((nuint)node, (nuint)value, id == HookId.TouchFieldMenuReplace))
                            owner.session.Enter((nuint)value);
                    }
                    else if (id == HookId.MenuManagerUpdate)
                    {
                        // The float delta occupies one raw stack word; forward its bits above.
                        // Several native focus changes invoke their closure directly, bypassing
                        // 1DD4C0. Sample only the owned manager, after its complete update.
                        var now = Environment.TickCount64;
                        if (owner.session.HasContext && now - owner.lastSample >= 80 &&
                            owner.source.OwnsManager(owner.session.Node, (nuint)node))
                        {
                            owner.lastSample = now;
                            owner.session.Refresh();
                        }
                    }
                    else if (id != HookId.SaveSlotDetailsRefresh || owner.source.IsSelectedSaveRecord((nuint)node, (nuint)value))
                        owner.session.Refresh((nuint)node);
                });
            };
            var hook = owner.factory.CreateHook(id, detour, address);
            original = hook.OriginalFunction ?? throw new InvalidOperationException("Submenu original is unavailable.");
            return new ReloadedPreparedHook<SubmenuNodeWordDelegate>(Name, hook, detour);
        }
        private IPreparedHook Open(nuint address, UnmanagedBoundaryGuard guard)
        {
            SaveSlotOpenDelegate? original = null;
            SaveSlotOpenDelegate detour = (node, mode, back) =>
            {
                byte result = 0;
                guard.Run(Name + " original", () => result = original!(node, mode, back));
                if (result != 0) owner.Observe(guard, Name, () => owner.session.Enter((nuint)node));
                return result;
            };
            var hook = owner.factory.CreateHook(id, detour, address);
            original = hook.OriginalFunction ?? throw new InvalidOperationException("Save open original is unavailable.");
            return new ReloadedPreparedHook<SaveSlotOpenDelegate>(Name, hook, detour);
        }
        private IPreparedHook Dispatch(nuint address, UnmanagedBoundaryGuard guard)
        {
            SubmenuManagerDispatchDelegate? original = null;
            SubmenuManagerDispatchDelegate detour = (manager, action, key) =>
            {
                guard.Run(Name + " original", () => original!(manager, action, key));
                owner.Observe(guard, Name, () =>
                {
                    if (owner.source.OwnsManager(owner.session.Node, (nuint)manager)) owner.session.Refresh();
                });
            };
            var hook = owner.factory.CreateHook(id, detour, address);
            original = hook.OriginalFunction ?? throw new InvalidOperationException("Menu dispatch original is unavailable.");
            return new ReloadedPreparedHook<SubmenuManagerDispatchDelegate>(Name, hook, detour);
        }
        private IPreparedHook FormationInit(nuint address, UnmanagedBoundaryGuard guard)
        {
            FormationSceneInitDelegate? original = null;
            FormationSceneInitDelegate detour = scene =>
            {
                byte result = 0;
                guard.Run(Name + " original", () => result = original!(scene));
                // The page exists only once the native init has built and attached it.
                if (result != 0) owner.Observe(guard, Name, () =>
                {
                    var page = owner.source.StandaloneFormation((nuint)scene);
                    (owner.formationScene, owner.formationPage) = ((nuint)scene, page);
                    if (page == 0)
                        owner.session.ReportUnavailable((nuint)scene, "Party", "Unable to read the party selection.");
                    else owner.session.Enter(page);
                });
                return result;
            };
            var hook = owner.factory.CreateHook(id, detour, address);
            original = hook.OriginalFunction ?? throw new InvalidOperationException("Party scene init original is unavailable.");
            return new ReloadedPreparedHook<FormationSceneInitDelegate>(Name, hook, detour);
        }
        private IPreparedHook FormationDestroy(nuint address, UnmanagedBoundaryGuard guard)
        {
            FormationSceneDestructorDelegate? original = null;
            FormationSceneDestructorDelegate detour = (scene, flags) =>
            {
                // Release the page while the scene still owns it; the original frees both.
                if ((nuint)scene == owner.formationScene) owner.Observe(guard, Name + " leave", () =>
                {
                    owner.session.CloseContext(owner.formationPage != 0 ? owner.formationPage : owner.formationScene);
                    owner.formationScene = owner.formationPage = 0;
                });
                nint result = 0;
                guard.Run(Name + " original", () => result = original!(scene, flags));
                return result;
            };
            var hook = owner.factory.CreateHook(id, detour, address);
            original = hook.OriginalFunction ?? throw new InvalidOperationException("Party scene destructor original is unavailable.");
            return new ReloadedPreparedHook<FormationSceneDestructorDelegate>(Name, hook, detour);
        }
    }
}

using System.Collections.ObjectModel;
using ChronoTriggerAccessibility.Core.Menus;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Hooks;
using ChronoTriggerAccessibility.Native.Memory;
using Reloaded.Hooks.Definitions;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>
/// Reads the Epoch's time gauge (<c>AgeSelectScene</c>) as a menu. The scene's own
/// keys are the native ones: up or right moves toward the End of Time, down or left
/// toward 65 000 000 BC, Confirm travels to a highlighted era other than the current
/// one, Cancel closes. The mod only narrates; it injects nothing here.
/// </summary>
public sealed class TimeGaugeHookSet : IHookActivationObserver
{
    public const string OwnerSource = "TimeGauge";
    public const string Title = "Time gauge";
    public const string KeyHelp = "Up or down chooses an era. Confirm travels there. Cancel closes.";

    private readonly IRuntimeNativeHookFactory hookFactory;
    private readonly ISemanticEventDispatcher dispatcher;
    private readonly TimeGaugeCapture capture;
    private readonly object gate = new();
    private readonly IReadOnlyList<IHookRegistration> registrations;
    private nuint imageBase;
    private UnmanagedBoundaryGuard? boundaryGuard;
    private bool active;
    private nuint scene;
    private int announcedSlot = -1;
    private bool closed;

    public TimeGaugeHookSet(IRuntimeNativeHookFactory hookFactory, IReadableMemory memory, ISemanticEventDispatcher dispatcher)
    {
        this.hookFactory = hookFactory ?? throw new ArgumentNullException(nameof(hookFactory));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        capture = new TimeGaugeCapture(memory ?? throw new ArgumentNullException(nameof(memory)));
        registrations = new ReadOnlyCollection<IHookRegistration>(
        [
            new Registration(this, HookId.TimeGaugeSceneInit, PrepareInit),
            new Registration(this, HookId.TimeGaugeSceneUpdate, PrepareUpdate),
        ]);
    }

    public IReadOnlyList<IHookRegistration> Registrations => registrations;

    public void AfterHooksActivated()
    {
        lock (gate)
        {
            if (imageBase == 0 || boundaryGuard is null)
                throw new InvalidOperationException("Time gauge hooks were not fully prepared before activation.");
            active = true;
        }
    }

    public void AfterHooksDisabled()
    {
        lock (gate) { active = false; scene = 0; announcedSlot = -1; closed = false; }
    }

    /// <summary>The gauge scene has finished building its seven items; the initial
    /// highlight is the era the Epoch is in, drawn grey and not selectable.</summary>
    public void OnInit(nuint sceneInstance)
    {
        lock (gate)
        {
            if (!active) return;
            var snapshot = capture.Capture(imageBase, sceneInstance);
            if (snapshot is null)
            {
                dispatcher.ReportCoverageFailure("The time gauge opened but its selection or era labels were unreadable.");
                return;
            }
            scene = sceneInstance; announcedSlot = snapshot.Slot; closed = false;
            dispatcher.Publish(new MenuPresented(new MenuOwner(OwnerSource, sceneInstance), Title, Focus(snapshot), [KeyHelp]));
        }
    }

    public void OnUpdate(nuint sceneInstance)
    {
        lock (gate)
        {
            if (!active || sceneInstance != scene || closed) return;
            var snapshot = capture.Capture(imageBase, sceneInstance);
            if (snapshot is null) return;
            if (snapshot.Closing)
            {
                closed = true;
                var owner = new MenuOwner(OwnerSource, sceneInstance);
                if (snapshot.Committed)
                {
                    var slot = TimeGaugeCapture.SlotForLocation(snapshot.Result);
                    var label = (slot is { } s ? capture.SlotLabel(imageBase, s) : null) ?? "the chosen era";
                    dispatcher.Publish(new MenuActivated(label));
                    dispatcher.Publish(new MenuExited(owner));
                    dispatcher.Publish(new NavigationAnnouncement(VehicleStoryRouting.Sentence($"Traveling to {label}")));
                }
                else
                {
                    dispatcher.Publish(new MenuExited(owner));
                    dispatcher.Publish(new NavigationAnnouncement("Time gauge closed."));
                }
                return;
            }
            if (snapshot.Slot == announcedSlot) return;
            announcedSlot = snapshot.Slot;
            dispatcher.Publish(new MenuFocusChanged(Focus(snapshot)));
        }
    }

    private static MenuFocus Focus(TimeGaugeSnapshot snapshot)
    {
        var current = snapshot.Slot == snapshot.CurrentEraSlot;
        var label = snapshot.Label ?? $"Era {snapshot.Slot + 1}";
        var help = current
            ? "Current era. Not selectable." + (snapshot.Description is null ? "" : " " + snapshot.Description)
            : snapshot.Description;
        return new(label, null, snapshot.Slot + 1, TimeGaugeSnapshot.SlotCount, help, current);
    }

    private IPreparedHook PrepareInit(nuint address)
    {
        IHook<TimeGaugeSceneInitDelegate>? hook = null;
        TimeGaugeSceneInitDelegate detour = sceneInstance =>
        {
            var result = hook!.OriginalFunction(sceneInstance);
            if (result != 0) Guard("AgeSelectScene::init", () => OnInit((nuint)sceneInstance));
            return result;
        };
        hook = hookFactory.CreateHook(HookId.TimeGaugeSceneInit, detour, address);
        return new ReloadedPreparedHook<TimeGaugeSceneInitDelegate>(GameVersionCatalog.Get(HookId.TimeGaugeSceneInit).Symbol, hook, detour);
    }

    private IPreparedHook PrepareUpdate(nuint address)
    {
        IHook<TimeGaugeSceneUpdateDelegate>? hook = null;
        TimeGaugeSceneUpdateDelegate detour = (sceneInstance, delta) =>
        {
            hook!.OriginalFunction(sceneInstance, delta);
            Guard("AgeSelectScene::update", () => OnUpdate((nuint)sceneInstance));
        };
        hook = hookFactory.CreateHook(HookId.TimeGaugeSceneUpdate, detour, address);
        return new ReloadedPreparedHook<TimeGaugeSceneUpdateDelegate>(GameVersionCatalog.Get(HookId.TimeGaugeSceneUpdate).Symbol, hook, detour);
    }

    private void Guard(string name, Action action)
    {
        var guard = boundaryGuard;
        if (guard is null || guard.IsFaulted) return;
        guard.Run(name, action);
    }

    private sealed class Registration(TimeGaugeHookSet owner, HookId id, Func<nuint, IPreparedHook> prepare) : IHookRegistration
    {
        public string Name => GameVersionCatalog.Get(id).Symbol;
        public IPreparedHook Prepare(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary)
        {
            var contract = GameVersionCatalog.Get(id);
            if (build.ImageBaseAddress == 0 || !build.HookAddresses.TryGetValue(id, out var address) ||
                address != checked(build.ImageBaseAddress + contract.Rva))
                throw new InvalidOperationException("The time gauge requires its verified native entry points.");
            lock (owner.gate) { owner.imageBase = build.ImageBaseAddress; owner.boundaryGuard = boundary; }
            return prepare(address);
        }
    }
}

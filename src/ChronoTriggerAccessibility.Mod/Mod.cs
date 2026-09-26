using System.Diagnostics;
using ChronoTriggerAccessibility.Core.Startup;
using ChronoTriggerAccessibility.Mod.Dialogue;
using ChronoTriggerAccessibility.Mod.Battle;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Extras;
using ChronoTriggerAccessibility.Mod.Intro;
using ChronoTriggerAccessibility.Mod.NewGame;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.SaveLoad;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Settings;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Mod.Template;
using ChronoTriggerAccessibility.Mod.TopMenu;
using ChronoTriggerAccessibility.Mod.Menus;
using ChronoTriggerAccessibility.Native.Memory;
using ChronoTriggerAccessibility.Native.Capture;
using Reloaded.Hooks.ReloadedII.Interfaces;

namespace ChronoTriggerAccessibility.Mod;

public sealed class Mod : ModBase
{
    private readonly IReloadedHooks? hooks;
    private readonly StartupTitleHookSet? startupTitleHookSet;
    private readonly NewGameHookSet? newGameHookSet;
    private readonly CompleteAccessibilityComposition? completeComposition;
    private readonly AccessibilityRuntime? runtime;
    private readonly Task? initializationTask;

    public Mod(ModContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        hooks = context.Hooks;

        var log = new ModLog(context.Logger, context.ModConfig.ModId);
        var fatalError = new AccessibleFatalError();
        var dispatcher = new SemanticEventDispatcher(log, fatalError);
        var nativeFactory = new ReloadedNativeHookFactory(hooks ?? throw new InvalidOperationException(
            "Reloaded shared hooks controller is unavailable."));
        var composition = CreateCompleteAccessibilityComposition(
            nativeFactory,
            nativeFactory,
            nativeFactory,
            new CurrentProcessReadableMemory(),
            dispatcher,
            new OpeningMovieTimeline());
        completeComposition = composition;
        startupTitleHookSet = composition.StartupTitleHookSet;
        newGameHookSet = composition.NewGameHookSet;
        runtime = new AccessibilityRuntime(
            new CurrentProcessExecutableVerifier(),
            new GameWindowWaiter(),
            new PrismRuntimeFactory(),
            composition.Installer,
            log,
            fatalError,
            Process.GetCurrentProcess().Id,
            dispatcher);

        // Reloaded calls the mod entry point on the game's startup thread. Window discovery must
        // execute on the thread pool so waiting for a visible window cannot deadlock that thread.
        initializationTask = runtime.StartInBackground();
        log.Info("Accessibility initialization scheduled off the game startup thread.");
    }

    public override void Disposing() => runtime?.Shutdown();

    public static StartupTitleComposition CreateStartupTitleComposition(
        IRuntimeNativeHookFactory hookFactory,
        IReadableMemory memory,
        ISemanticEventDispatcher dispatcher,
        OpeningMovieTimeline movieTimeline)
    {
        var hookSet = new StartupTitleHookSet(hookFactory, memory, dispatcher, movieTimeline);
        return new StartupTitleComposition(
            hookSet,
            new ReloadedHookInstaller(hookSet.Registrations, [hookSet]));
    }

    public static AccessibilityComposition CreateAccessibilityComposition(
        IRuntimeNativeHookFactory hookFactory,
        IRuntimeNativeFunctionWrapperFactory wrapperFactory,
        IReadableMemory memory,
        ISemanticEventDispatcher dispatcher,
        OpeningMovieTimeline movieTimeline)
    {
        ArgumentNullException.ThrowIfNull(hookFactory);
        ArgumentNullException.ThrowIfNull(wrapperFactory);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(movieTimeline);

        var sharedFanout = new SharedNativeHookFanoutFactory(
            hookFactory,
            dispatcher.ReportCoverageFailure);
        var newGame = new NewGameHookSet(sharedFanout, wrapperFactory, memory, dispatcher);
        var startupTitle = new StartupTitleHookSet(
            sharedFanout,
            memory,
            dispatcher,
            movieTimeline);
        sharedFanout.ConfigureObservers([startupTitle, newGame]);
        var registrations = startupTitle.Registrations.Concat(newGame.Registrations).ToArray();
        var installer = new ReloadedHookInstaller(registrations, [startupTitle, newGame]);
        return new AccessibilityComposition(startupTitle, newGame, installer);
    }

    public static CompleteAccessibilityComposition CreateCompleteAccessibilityComposition(
        IRuntimeNativeHookFactory hookFactory,
        IRuntimeNativeAsmHookFactory asmHookFactory,
        IRuntimeNativeFunctionWrapperFactory wrapperFactory,
        IReadableMemory memory,
        ISemanticEventDispatcher dispatcher,
        OpeningMovieTimeline movieTimeline)
    {
        ArgumentNullException.ThrowIfNull(hookFactory);
        ArgumentNullException.ThrowIfNull(asmHookFactory);
        ArgumentNullException.ThrowIfNull(wrapperFactory);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(movieTimeline);

        var introRecorder = new IntroTraceRecorder(memory, dispatcher.RecordDiagnostic);
        dispatcher = new IntroTraceDispatcher(dispatcher, introRecorder);
        var navigationSpeech = dispatcher;
        nuint navigationImageBase = 0;
        var navigationText = new NavigationTextCapture(memory);
        var worldSource = new WorldNavigationSource(memory, dispatcher.RecordDiagnostic);
        var areas = new NavigationAreaAnnouncer(
            text => navigationSpeech.Publish(new NavigationAnnouncement(text)), dispatcher.RecordDiagnostic);
        var footstepSound = new FootstepSound(navigationSpeech.RecordDiagnostic,
            () => navigationSpeech.Publish(new NavigationAnnouncement("Footstep audio is unavailable.")),
            timingInterrupted: () => navigationSpeech.Publish(new NavigationAnnouncement(
                "Footstep timing interrupted. Stop and press K for a fresh count.")));
        var motionCaptureStage = "not sampled";
        var footsteps = new FieldFootstepRuntime(engine =>
        {
            var motion = FieldMotionCapture.Capture(memory, (nuint)engine, out motionCaptureStage);
            return motion is { } value ? new FootstepFrame(
                ((ulong)value.Engine << 32) | value.ActorBase, value.Scene, value.Actor, value.FineX, value.FineY) : null;
        }, NavigationKeyboard.IsGameForeground, NavigationKeyboard.IsKeyDown, () => Environment.TickCount64,
            footstepSound.Play, footstepSound.Stop,
            text => navigationSpeech.Publish(new NavigationAnnouncement(text)), dispatcher.RecordDiagnostic,
            () => $"field={motionCaptureStage}; world={worldSource.MotionStage}", worldSource.CaptureMotion);
        var navigationSource = new FieldNavigationSource(memory, dispatcher.RecordDiagnostic, scene =>
        {
            var name = navigationText.FieldName(navigationImageBase, scene);
            return string.IsNullOrWhiteSpace(name) || name.StartsWith('(') ? "Local area" : name;
        });
        NavigationFrame? Field(nint engine) => areas.Observe(navigationSource.Capture(engine));
        NavigationFrame? World(nint context) => areas.Observe(worldSource.Capture(context));
        NavigationFrame? Flight(nint context, VehicleKind kind) => areas.Observe(worldSource.CaptureFlight(context, kind));
        var prompts = new VehiclePromptAnnouncer(text => navigationSpeech.Publish(new NavigationAnnouncement(text)));
        var navigation = new FieldNavigationRuntime(Field, new NavigationKeyboard(),
            NavigationKeyboard.IsGameForeground, () => Environment.TickCount64,
            text => navigationSpeech.Publish(new NavigationAnnouncement(text)), dispatcher.RecordDiagnostic,
            engine => Field(engine), () => { navigationSource.Reset(); worldSource.Reset(); areas.Reset(); prompts.Reset(); },
            footsteps.Suspend, World, context => World(context), footsteps.SetGuidance,
            Flight, worldSource.IsVehicleActive);
        var battleKeyboard = new BattleKeyboard();
        var battle = new BattleRuntime(navigationSpeech.Publish, battleKeyboard,
            NavigationKeyboard.IsGameForeground, _ =>
            {
                navigation.Suspend("battle state changed");
                footsteps.Suspend();
            });
        var battleSession = new BattleSession(memory, battle, dispatcher.RecordDiagnostic);
        var battleHooks = new BattleHookSet(hookFactory, battleSession.Tick, battleSession.Close,
            battleSession.Message, battleSession.Number, battleSession.Miss, battleSession.Render,
            battleSession.BindImageBase, battleSession.Disable);
        var battleKeyboardHooks = new BattleKeyboardHookSet(asmHookFactory, battleKeyboard, () => battle.IsActive);
        var navigationHooks = new FieldNavigationHookSet(asmHookFactory, (engine, pad) =>
        {
            if (battle.IsActive) return pad;
            var accepted = navigation.OnInput(engine, pad);
            footsteps.OnInput(engine, accepted);
            return accepted;
        }, () => { footstepSound.WarmUp(); navigation.Enable(); footsteps.Enable(); },
            () => { navigation.Disable(); footsteps.Disable(); });
        var worldHooks = new WorldNavigationHookSet(asmHookFactory, (context, pad) =>
        {
            if (battle.IsActive) return;
            var accepted = navigation.OnWorldInput(context, pad);
            footsteps.OnWorldInput(context, accepted);
            var motion = worldSource.CaptureMotion(context);
            prompts.Observe(context, worldSource.CaptureVehicles(context),
                motion is { } walking ? walking.X / 16 : -1, motion is { } y ? y.Y / 16 : -1);
        }, (context, pad) => battle.IsActive ? pad : navigation.ApplyWorldPad(context, pad), image =>
        {
            navigationImageBase = image;
            worldSource.BindImageBase(image);
        }, (context, pad, kind) =>
        {
            // Vehicle ticks fire for parked vehicles too; only the active flying
            // transport may pump navigation, and flight never reaches the footsteps.
            if (battle.IsActive || !navigation.OnVehicleInput(context, pad, kind)) return;
            prompts.Observe(context, worldSource.CaptureVehicles(context));
        }, (context, pad, kind) => battle.IsActive ? pad : navigation.ApplyVehiclePad(context, pad, kind));
        dispatcher = new NavigationDispatcher(dispatcher, navigation);
        var timeGauge = new TimeGaugeHookSet(hookFactory, memory, dispatcher);

        var sharedFanout = new SharedNativeHookFanoutFactory(
            hookFactory,
            dispatcher.ReportCoverageFailure);
        var newGame = new NewGameHookSet(sharedFanout, wrapperFactory, memory, dispatcher);
        var extras = new ExtrasHookSet(sharedFanout, memory, dispatcher);
        var steamSettings = new SteamSettingsHookSet(sharedFanout, asmHookFactory, memory, dispatcher);
        var touchSettings = new TouchSettingsHookSet(sharedFanout, asmHookFactory, memory, dispatcher);
        var topMenu = new TopMenuHookSet(sharedFanout, asmHookFactory, memory, dispatcher);
        var submenuSource = new FieldSubmenuSource(memory);
        var submenuSession = new FieldSubmenuSession(submenuSource.Capture, submenuSource.Title,
            submenuSource.ConfirmationActive, NavigationKeyboard.IsGameForeground, dispatcher.Publish, dispatcher.RecordDiagnostic);
        var submenuHooks = new FieldSubmenuHookSet(hookFactory, submenuSource, submenuSession);
        var shopHooks = new ShopHookSet(hookFactory, memory, dispatcher, NavigationKeyboard.IsGameForeground);
        topMenu.SubmenuOwnsSpeech = () => submenuSession.HasContext || shopHooks.HasContext;
        var saveLoad = new SaveLoadConfirmationHookSet(sharedFanout, memory, dispatcher,
            node => submenuSession.Close(node), submenuSession.Pause);
        var endingResult = new EndingResultHookSet(sharedFanout, memory, dispatcher);
        var restartNotice = new SteamSettingsRestartNoticeHookSet(sharedFanout, memory, dispatcher);

        var startupTitle = new StartupTitleHookSet(
            sharedFanout,
            memory,
            dispatcher,
            movieTimeline,
            titleActionCompleted: action =>
            {
                if (action == 5)
                {
                    steamSettings.FlushDeferredPresentation();
                }
            });

        // Root shared-hook owners are constructed only after the complete, ordered observer
        // list is frozen. Extras must observe the shared Touch destructor before TopMenu's root.
        sharedFanout.ConfigureObservers(
            [startupTitle, newGame, extras, steamSettings, touchSettings, topMenu, saveLoad, endingResult,
                restartNotice]);
        var dialogue = new DialogueHookSet(sharedFanout, asmHookFactory, memory, dispatcher);
        var introTrace = new IntroTraceHookSet(sharedFanout, introRecorder);
        var registrations = startupTitle.Registrations
            .Concat(newGame.Registrations)
            .Concat(extras.Registrations)
            .Concat(steamSettings.Registrations)
            .Concat(touchSettings.Registrations)
            .Concat(topMenu.Registrations)
            .Concat(saveLoad.Registrations)
            .Concat(endingResult.Registrations)
            .Concat(restartNotice.Registrations)
            .Concat(dialogue.Registrations)
            .Concat(introTrace.Registrations)
            .Concat(navigationHooks.Registrations)
            .Concat(worldHooks.Registrations)
            .Concat(timeGauge.Registrations)
            .Concat(battleHooks.Registrations)
            .Concat(battleKeyboardHooks.Registrations)
            .Concat(submenuHooks.Registrations)
            .Concat(shopHooks.Registrations)
            .ToArray();
        var participants = new IHookActivationObserver[]
        {
            startupTitle,
            newGame,
            extras,
            steamSettings,
            touchSettings,
            topMenu,
            saveLoad,
            endingResult,
            restartNotice,
            dialogue,
            introTrace,
            navigationHooks,
            worldHooks,
            timeGauge,
            battleHooks,
            battleKeyboardHooks,
            submenuHooks,
            shopHooks,
        };
        var installer = new ReloadedHookInstaller(registrations, participants);
        return new CompleteAccessibilityComposition(
            startupTitle,
            newGame,
            extras,
            steamSettings,
            touchSettings,
            topMenu,
            saveLoad,
            dialogue,
            introTrace,
            navigationHooks,
            worldHooks,
            timeGauge,
            battleHooks,
            submenuHooks,
            shopHooks,
            installer);
    }

    public Mod() { }
}

public sealed record StartupTitleComposition(
    StartupTitleHookSet HookSet,
    ReloadedHookInstaller Installer);

public sealed record AccessibilityComposition(
    StartupTitleHookSet StartupTitleHookSet,
    NewGameHookSet NewGameHookSet,
    ReloadedHookInstaller Installer);

public sealed record CompleteAccessibilityComposition(
    StartupTitleHookSet StartupTitleHookSet,
    NewGameHookSet NewGameHookSet,
    ExtrasHookSet ExtrasHookSet,
    SteamSettingsHookSet SteamSettingsHookSet,
    TouchSettingsHookSet TouchSettingsHookSet,
    TopMenuHookSet TopMenuHookSet,
    SaveLoadConfirmationHookSet SaveLoadConfirmationHookSet,
    DialogueHookSet DialogueHookSet,
    IntroTraceHookSet IntroTraceHookSet,
    FieldNavigationHookSet FieldNavigationHookSet,
    WorldNavigationHookSet WorldNavigationHookSet,
    TimeGaugeHookSet TimeGaugeHookSet,
    BattleHookSet BattleHookSet,
    FieldSubmenuHookSet FieldSubmenuHookSet,
    ShopHookSet ShopHookSet,
    ReloadedHookInstaller Installer);

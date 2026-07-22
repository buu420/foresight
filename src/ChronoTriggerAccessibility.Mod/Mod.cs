using System.Diagnostics;
using ChronoTriggerAccessibility.Core.Startup;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.NewGame;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Mod.Template;
using ChronoTriggerAccessibility.Native.Memory;
using Reloaded.Hooks.ReloadedII.Interfaces;

namespace ChronoTriggerAccessibility.Mod;

public sealed class Mod : ModBase
{
    private readonly IReloadedHooks? hooks;
    private readonly StartupTitleHookSet? startupTitleHookSet;
    private readonly NewGameHookSet? newGameHookSet;
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
        var composition = CreateAccessibilityComposition(
            nativeFactory,
            nativeFactory,
            new CurrentProcessReadableMemory(),
            dispatcher,
            new OpeningMovieTimeline());
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

        var newGame = new NewGameHookSet(hookFactory, wrapperFactory, memory, dispatcher);
        var sharedFanout = new SharedNativeHookFanoutFactory(
            hookFactory,
            newGame,
            dispatcher.ReportCoverageFailure);
        var startupTitle = new StartupTitleHookSet(
            sharedFanout,
            memory,
            dispatcher,
            movieTimeline);
        var registrations = startupTitle.Registrations.Concat(newGame.Registrations).ToArray();
        var installer = new ReloadedHookInstaller(registrations, [startupTitle, newGame]);
        return new AccessibilityComposition(startupTitle, newGame, installer);
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

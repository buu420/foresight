using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Hooks;
using Reloaded.Hooks.Definitions;

namespace ChronoTriggerAccessibility.Mod.NewGame;

public interface INewGameSharedNativeObserver
{
    void AfterTextManagerGetMsg(
        nint textManager,
        nint result,
        int fileId,
        int messageId,
        nint returned);

    void AfterFocusSet(nint manager, int managerKey);
}

/// <summary>
/// Adds New Game observations to the two native hooks already owned by the startup/title set.
/// The startup/title detour remains the sole path to the native original.
/// </summary>
public sealed class SharedNativeHookFanoutFactory : IRuntimeNativeHookFactory
{
    private readonly IRuntimeNativeHookFactory inner;
    private readonly INewGameSharedNativeObserver observer;
    private readonly Action<string> reportFailure;

    public SharedNativeHookFanoutFactory(
        IRuntimeNativeHookFactory inner,
        INewGameSharedNativeObserver observer,
        Action<string> reportFailure)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.observer = observer ?? throw new ArgumentNullException(nameof(observer));
        this.reportFailure = reportFailure ?? throw new ArgumentNullException(nameof(reportFailure));
    }

    public IHook<TDelegate> CreateHook<TDelegate>(HookId id, TDelegate detour, nuint address)
        where TDelegate : Delegate
    {
        ArgumentNullException.ThrowIfNull(detour);
        return id switch
        {
            HookId.TextManagerGetMsg when detour is TextManagerGetMsgDelegate textDetour =>
                CastHook<TDelegate, TextManagerGetMsgDelegate>(CreateTextHook(textDetour, address)),
            HookId.NsMenuFocusSetter when detour is NsMenuFocusSetterDelegate focusDetour =>
                CastHook<TDelegate, NsMenuFocusSetterDelegate>(CreateFocusHook(focusDetour, address)),
            HookId.TextManagerGetMsg or HookId.NsMenuFocusSetter => throw new InvalidOperationException(
                $"Shared hook {id} was requested with incompatible delegate {typeof(TDelegate).FullName}."),
            _ => inner.CreateHook(id, detour, address),
        };
    }

    private IHook<TextManagerGetMsgDelegate> CreateTextHook(
        TextManagerGetMsgDelegate startupDetour,
        nuint address)
    {
        TextManagerGetMsgDelegate fanout = (textManager, result, fileId, messageId) =>
        {
            var returned = startupDetour(textManager, result, fileId, messageId);
            ObserveSafely(
                "Shared TextManager::getMsg New Game observer failed",
                () => observer.AfterTextManagerGetMsg(
                    textManager, result, fileId, messageId, returned));
            return returned;
        };
        var nativeHook = inner.CreateHook(HookId.TextManagerGetMsg, fanout, address);
        return new RootedFanoutHook<TextManagerGetMsgDelegate>(nativeHook, fanout);
    }

    private IHook<NsMenuFocusSetterDelegate> CreateFocusHook(
        NsMenuFocusSetterDelegate startupDetour,
        nuint address)
    {
        NsMenuFocusSetterDelegate fanout = (manager, managerKey) =>
        {
            startupDetour(manager, managerKey);
            ObserveSafely(
                "Shared nsMenu focus New Game observer failed",
                () => observer.AfterFocusSet(manager, managerKey));
        };
        var nativeHook = inner.CreateHook(HookId.NsMenuFocusSetter, fanout, address);
        return new RootedFanoutHook<NsMenuFocusSetterDelegate>(nativeHook, fanout);
    }

    private void ObserveSafely(string prefix, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            try
            {
                reportFailure($"{prefix}: {exception.GetType().Name}: {exception.Message}");
            }
            catch (Exception)
            {
                // Neither New Game instrumentation nor its diagnostic sink may escape into
                // the already-successful startup/title detour.
            }
        }
    }

    private static IHook<TRequested> CastHook<TRequested, TActual>(IHook<TActual> hook)
        where TRequested : Delegate
        where TActual : Delegate => (IHook<TRequested>)(object)hook;

    private sealed class RootedFanoutHook<TDelegate>(IHook<TDelegate> inner, TDelegate detourRoot)
        : IHook<TDelegate>
        where TDelegate : Delegate
    {
        private readonly IHook<TDelegate> inner = inner ?? throw new ArgumentNullException(nameof(inner));
        private readonly TDelegate detourRoot = detourRoot ?? throw new ArgumentNullException(nameof(detourRoot));

        public TDelegate OriginalFunction => inner.OriginalFunction;
        public IReverseWrapper<TDelegate> ReverseWrapper => inner.ReverseWrapper;
        public bool IsHookEnabled => inner.IsHookEnabled;
        public bool IsHookActivated => inner.IsHookActivated;
        public nint OriginalFunctionAddress => inner.OriginalFunctionAddress;
        public nint OriginalFunctionWrapperAddress => inner.OriginalFunctionWrapperAddress;

        public IHook<TDelegate> Activate()
        {
            inner.Activate();
            GC.KeepAlive(detourRoot);
            return this;
        }

        IHook IHook.Activate() => Activate();
        public void Disable() => inner.Disable();
        public void Enable() => inner.Enable();
    }
}

using System.Collections.Immutable;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Hooks;
using Reloaded.Hooks.Definitions;

namespace ChronoTriggerAccessibility.Mod.NewGame;

public interface ISharedNativeHookObserver
{
    void AfterTextManagerGetMsg(nint textManager, nint result, int fileId, int messageId, nint returned) { }
    void AfterFocusSet(nint manager, int managerKey) { }
    void AfterCustomButtonConstructed(nint storage, nint returned) { }
    void AfterControlBound(nint manager, nint focusableState, int managerKey) { }
    void BeforeTouchTopMenuDeletingDestructor(nint node, uint deletingFlags) { }
}

/// <summary>
/// Adds ordered observations to native hooks already owned by another hook set.
/// The supplied hook-set detour remains the sole path to the native original.
/// </summary>
public sealed class SharedNativeHookFanoutFactory : IRuntimeNativeHookFactory
{
    private readonly IRuntimeNativeHookFactory inner;
    private readonly ImmutableArray<ISharedNativeHookObserver> observers;
    private readonly Action<string> reportFailure;

    public SharedNativeHookFanoutFactory(
        IRuntimeNativeHookFactory inner,
        ISharedNativeHookObserver observer,
        Action<string> reportFailure)
        : this(inner, new[] { observer ?? throw new ArgumentNullException(nameof(observer)) }, reportFailure)
    {
    }

    public SharedNativeHookFanoutFactory(
        IRuntimeNativeHookFactory inner,
        IEnumerable<ISharedNativeHookObserver> observers,
        Action<string> reportFailure)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        ArgumentNullException.ThrowIfNull(observers);
        this.observers = observers.ToImmutableArray();
        if (this.observers.Any(observer => observer is null))
        {
            throw new ArgumentNullException(nameof(observers), "Shared hook observers cannot contain null elements.");
        }

        this.reportFailure = reportFailure ?? throw new ArgumentNullException(nameof(reportFailure));
    }

    public IHook<TDelegate> CreateHook<TDelegate>(HookId id, TDelegate detour, nuint address)
        where TDelegate : Delegate
    {
        ArgumentNullException.ThrowIfNull(detour);
        return id switch
        {
            HookId.TextManagerGetMsg when typeof(TDelegate) == typeof(TextManagerGetMsgDelegate) =>
                CastHook<TDelegate, TextManagerGetMsgDelegate>(CreateTextHook((TextManagerGetMsgDelegate)(Delegate)detour, address)),
            HookId.NsMenuFocusSetter when typeof(TDelegate) == typeof(NsMenuFocusSetterDelegate) =>
                CastHook<TDelegate, NsMenuFocusSetterDelegate>(CreateFocusHook((NsMenuFocusSetterDelegate)(Delegate)detour, address)),
            HookId.NsMenuCustomButtonConstructor when typeof(TDelegate) == typeof(NsMenuCustomButtonConstructorDelegate) =>
                CastHook<TDelegate, NsMenuCustomButtonConstructorDelegate>(CreateCustomButtonHook((NsMenuCustomButtonConstructorDelegate)(Delegate)detour, address)),
            HookId.NsMenuControlBinder when typeof(TDelegate) == typeof(NsMenuControlBinderDelegate) =>
                CastHook<TDelegate, NsMenuControlBinderDelegate>(CreateControlBinderHook((NsMenuControlBinderDelegate)(Delegate)detour, address)),
            HookId.TouchTopMenuDeletingDestructor when typeof(TDelegate) == typeof(TouchTopMenuDeletingDestructorDelegate) =>
                CastHook<TDelegate, TouchTopMenuDeletingDestructorDelegate>(CreateTouchDeletingDestructorHook(
                    (TouchTopMenuDeletingDestructorDelegate)(Delegate)detour, address)),
            HookId.TextManagerGetMsg or HookId.NsMenuFocusSetter or HookId.NsMenuCustomButtonConstructor or
                HookId.NsMenuControlBinder or HookId.TouchTopMenuDeletingDestructor =>
                throw new InvalidOperationException($"Shared hook {id} was requested with incompatible delegate {typeof(TDelegate).FullName}."),
            _ => inner.CreateHook(id, detour, address),
        };
    }

    private IHook<TextManagerGetMsgDelegate> CreateTextHook(TextManagerGetMsgDelegate root, nuint address)
    {
        TextManagerGetMsgDelegate fanout = (textManager, result, fileId, messageId) =>
        {
            var returned = root(textManager, result, fileId, messageId);
            ObserveAll("TextManager::getMsg", observer => observer.AfterTextManagerGetMsg(textManager, result, fileId, messageId, returned));
            return returned;
        };
        return Root(HookId.TextManagerGetMsg, fanout, address);
    }

    private IHook<NsMenuFocusSetterDelegate> CreateFocusHook(NsMenuFocusSetterDelegate root, nuint address)
    {
        NsMenuFocusSetterDelegate fanout = (manager, managerKey) =>
        {
            root(manager, managerKey);
            ObserveAll("nsMenu focus setter", observer => observer.AfterFocusSet(manager, managerKey));
        };
        return Root(HookId.NsMenuFocusSetter, fanout, address);
    }

    private IHook<NsMenuCustomButtonConstructorDelegate> CreateCustomButtonHook(NsMenuCustomButtonConstructorDelegate root, nuint address)
    {
        NsMenuCustomButtonConstructorDelegate fanout = storage =>
        {
            var returned = root(storage);
            ObserveAll("nsMenu CustomButton constructor", observer => observer.AfterCustomButtonConstructed(storage, returned));
            return returned;
        };
        return Root(HookId.NsMenuCustomButtonConstructor, fanout, address);
    }

    private IHook<NsMenuControlBinderDelegate> CreateControlBinderHook(NsMenuControlBinderDelegate root, nuint address)
    {
        NsMenuControlBinderDelegate fanout = (manager, focusableState, managerKey) =>
        {
            root(manager, focusableState, managerKey);
            ObserveAll("nsMenu control binder", observer => observer.AfterControlBound(manager, focusableState, managerKey));
        };
        return Root(HookId.NsMenuControlBinder, fanout, address);
    }

    private IHook<TouchTopMenuDeletingDestructorDelegate> CreateTouchDeletingDestructorHook(
        TouchTopMenuDeletingDestructorDelegate root,
        nuint address)
    {
        TouchTopMenuDeletingDestructorDelegate fanout = (node, deletingFlags) =>
        {
            ObserveAll(
                "Touch top/Ending Detail deleting destructor",
                observer => observer.BeforeTouchTopMenuDeletingDestructor(node, deletingFlags));
            return root(node, deletingFlags);
        };
        return Root(HookId.TouchTopMenuDeletingDestructor, fanout, address);
    }

    private IHook<TDelegate> Root<TDelegate>(HookId id, TDelegate fanout, nuint address)
        where TDelegate : Delegate => new RootedFanoutHook<TDelegate>(inner.CreateHook(id, fanout, address), fanout);

    private void ObserveAll(string hookName, Action<ISharedNativeHookObserver> observe)
    {
        foreach (var observer in observers)
        {
            try
            {
                observe(observer);
            }
            catch (Exception exception)
            {
                try
                {
                    reportFailure($"Shared {hookName} observer failed: {exception.GetType().Name}: {exception.Message}");
                }
                catch (Exception)
                {
                    // Diagnostics must not escape into a successfully completed native detour.
                }
            }
        }
    }

    private static IHook<TRequested> CastHook<TRequested, TActual>(IHook<TActual> hook)
        where TRequested : Delegate
        where TActual : Delegate => (IHook<TRequested>)(object)hook;

    private sealed class RootedFanoutHook<TDelegate>(IHook<TDelegate> inner, TDelegate detourRoot) : IHook<TDelegate>
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
        public IHook<TDelegate> Activate() { inner.Activate(); GC.KeepAlive(detourRoot); return this; }
        IHook IHook.Activate() => Activate();
        public void Disable() => inner.Disable();
        public void Enable() => inner.Enable();
    }
}

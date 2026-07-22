using System.Collections;
using ChronoTriggerAccessibility.Mod.NewGame;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Hooks;
using Reloaded.Hooks.Definitions;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.NewGame;

public sealed class SharedNativeHookFanoutFactoryTests
{
    [Fact]
    public void TextHookForwardsEveryNativeArgumentAndRootReturnToEveryObserver()
    {
        var calls = new List<string>();
        var first = new PayloadObserver("first", calls);
        var second = new PayloadObserver("second", calls);
        var inner = new RecordingFactory();
        var factory = CreateFactory(inner, calls, first, second);
        nint textManager = 0x1011;
        nint result = 0x2022;
        const int fileId = 0x3033;
        const int messageId = 0x4044;
        nint returned = 0x5055;
        var rootCalls = 0;
        TextManagerGetMsgDelegate root = (actualTextManager, actualResult, actualFileId, actualMessageId) =>
        {
            rootCalls++;
            calls.Add("root-text");
            Assert.Equal(textManager, actualTextManager);
            Assert.Equal(result, actualResult);
            Assert.Equal(fileId, actualFileId);
            Assert.Equal(messageId, actualMessageId);
            return returned;
        };

        _ = factory.CreateHook(HookId.TextManagerGetMsg, root, 0x5B9110);
        var actualReturned = inner.GetDetour<TextManagerGetMsgDelegate>(HookId.TextManagerGetMsg)(
            textManager, result, fileId, messageId);

        Assert.Equal(returned, actualReturned);
        Assert.Equal(1, rootCalls);
        Assert.Equal(["root-text", "first-text", "second-text"], calls);
        var expected = new TextPayload(textManager, result, fileId, messageId, returned);
        Assert.Equal(expected, Assert.Single(first.TextPayloads));
        Assert.Equal(expected, Assert.Single(second.TextPayloads));
    }

    [Fact]
    public void CustomButtonHookForwardsStorageAndRootReturnToEveryObserver()
    {
        var calls = new List<string>();
        var first = new PayloadObserver("first", calls);
        var second = new PayloadObserver("second", calls);
        var inner = new RecordingFactory();
        var factory = CreateFactory(inner, calls, first, second);
        nint storage = 0x6161;
        nint returned = 0x7272;
        var rootCalls = 0;
        NsMenuCustomButtonConstructorDelegate root = actualStorage =>
        {
            rootCalls++;
            calls.Add("root-custom");
            Assert.Equal(storage, actualStorage);
            return returned;
        };

        _ = factory.CreateHook(HookId.NsMenuCustomButtonConstructor, root, 0x5D2160);
        var actualReturned = inner.GetDetour<NsMenuCustomButtonConstructorDelegate>(
            HookId.NsMenuCustomButtonConstructor)(storage);

        Assert.Equal(returned, actualReturned);
        Assert.Equal(1, rootCalls);
        Assert.Equal(["root-custom", "first-custom", "second-custom"], calls);
        var expected = new CustomButtonPayload(storage, returned);
        Assert.Equal(expected, Assert.Single(first.CustomButtonPayloads));
        Assert.Equal(expected, Assert.Single(second.CustomButtonPayloads));
    }

    [Fact]
    public void FocusHookForwardsManagerAndKeyToRootAndEveryObserver()
    {
        var calls = new List<string>();
        var first = new PayloadObserver("first", calls);
        var second = new PayloadObserver("second", calls);
        var inner = new RecordingFactory();
        var factory = CreateFactory(inner, calls, first, second);
        nint manager = 0x8383;
        const int managerKey = 0x9494;
        var rootCalls = 0;
        NsMenuFocusSetterDelegate root = (actualManager, actualManagerKey) =>
        {
            rootCalls++;
            calls.Add("root-focus");
            Assert.Equal(manager, actualManager);
            Assert.Equal(managerKey, actualManagerKey);
        };

        _ = factory.CreateHook(HookId.NsMenuFocusSetter, root, 0x5DD3E0);
        inner.GetDetour<NsMenuFocusSetterDelegate>(HookId.NsMenuFocusSetter)(manager, managerKey);

        Assert.Equal(1, rootCalls);
        Assert.Equal(["root-focus", "first-focus", "second-focus"], calls);
        var expected = new FocusPayload(manager, managerKey);
        Assert.Equal(expected, Assert.Single(first.FocusPayloads));
        Assert.Equal(expected, Assert.Single(second.FocusPayloads));
    }

    [Fact]
    public void ControlBinderHookForwardsEveryNativeArgumentToRootAndEveryObserver()
    {
        var calls = new List<string>();
        var first = new PayloadObserver("first", calls);
        var second = new PayloadObserver("second", calls);
        var inner = new RecordingFactory();
        var factory = CreateFactory(inner, calls, first, second);
        nint manager = 0xA5A5;
        nint focusableState = 0xB6B6;
        const int managerKey = 0xC7C7;
        var rootCalls = 0;
        NsMenuControlBinderDelegate root = (actualManager, actualFocusableState, actualManagerKey) =>
        {
            rootCalls++;
            calls.Add("root-binder");
            Assert.Equal(manager, actualManager);
            Assert.Equal(focusableState, actualFocusableState);
            Assert.Equal(managerKey, actualManagerKey);
        };

        _ = factory.CreateHook(HookId.NsMenuControlBinder, root, 0x5DD260);
        inner.GetDetour<NsMenuControlBinderDelegate>(HookId.NsMenuControlBinder)(
            manager, focusableState, managerKey);

        Assert.Equal(1, rootCalls);
        Assert.Equal(["root-binder", "first-binder", "second-binder"], calls);
        var expected = new BinderPayload(manager, focusableState, managerKey);
        Assert.Equal(expected, Assert.Single(first.BinderPayloads));
        Assert.Equal(expected, Assert.Single(second.BinderPayloads));
    }

    [Fact]
    public void TouchDeletingDestructorNotifiesEveryObserverBeforeRootAndPreservesReturn()
    {
        var calls = new List<string>();
        var first = new PayloadObserver("first", calls);
        var second = new PayloadObserver("second", calls);
        var inner = new RecordingFactory();
        var factory = CreateFactory(inner, calls, first, second);
        nint node = 0xD8D8;
        const uint deletingFlags = 0xAABBCCDD;
        nint returned = 0xE9E9;
        var rootCalls = 0;
        TouchTopMenuDeletingDestructorDelegate root = (actualNode, actualFlags) =>
        {
            rootCalls++;
            calls.Add("root-touch-delete");
            Assert.Equal(node, actualNode);
            Assert.Equal(deletingFlags, actualFlags);
            return returned;
        };

        _ = factory.CreateHook(HookId.TouchTopMenuDeletingDestructor, root, 0x5D2690);
        var actualReturned = inner.GetDetour<TouchTopMenuDeletingDestructorDelegate>(
            HookId.TouchTopMenuDeletingDestructor)(node, deletingFlags);

        Assert.Equal(returned, actualReturned);
        Assert.Equal(1, rootCalls);
        Assert.Equal(["first-touch-delete", "second-touch-delete", "root-touch-delete"], calls);
        var expected = new DeletingDestructorPayload(node, deletingFlags);
        Assert.Equal(expected, Assert.Single(first.DeletingDestructorPayloads));
        Assert.Equal(expected, Assert.Single(second.DeletingDestructorPayloads));
    }

    [Theory]
    [InlineData(HookId.TextManagerGetMsg)]
    [InlineData(HookId.NsMenuFocusSetter)]
    [InlineData(HookId.NsMenuCustomButtonConstructor)]
    [InlineData(HookId.NsMenuControlBinder)]
    public void RootFaultForEverySharedShapeEscapesWithoutObserversOrFailureReports(HookId id)
    {
        var calls = new List<string>();
        var inner = new RecordingFactory();
        var factory = CreateFactory(inner, calls, new MarkerObserver("observer", calls));
        var expected = new InvalidOperationException($"root-{id}");

        var actual = CreateAndInvokeThrowingRoot(id, factory, inner, expected);

        Assert.Same(expected, actual);
        Assert.Empty(calls);
    }

    [Theory]
    [InlineData(HookId.TextManagerGetMsg, "text")]
    [InlineData(HookId.NsMenuFocusSetter, "focus")]
    [InlineData(HookId.NsMenuCustomButtonConstructor, "custom")]
    [InlineData(HookId.NsMenuControlBinder, "binder")]
    public void ObserverAndReporterFaultsAreIsolatedForEverySharedShape(HookId id, string shape)
    {
        var calls = new List<string>();
        var inner = new RecordingFactory();
        var factory = new SharedNativeHookFanoutFactory(
            inner,
            new ISharedNativeHookObserver[]
            {
                new ThrowingObserver("first", calls),
                new MarkerObserver("middle", calls),
                new ThrowingObserver("second", calls),
            },
            message =>
            {
                calls.Add($"report:{message}");
                throw new ApplicationException("reporter fault");
            });

        CreateAndInvokeSuccessfulRoot(id, factory, inner, calls);

        Assert.Collection(
            calls,
            item => Assert.Equal("root", item),
            item => Assert.Equal($"first-{shape}", item),
            item => Assert.Contains($"InvalidOperationException: first-{shape}-fault", item),
            item => Assert.Equal($"middle-{shape}", item),
            item => Assert.Equal($"second-{shape}", item),
            item => Assert.Contains($"InvalidOperationException: second-{shape}-fault", item));
        Assert.Equal(2, calls.Count(item => item.StartsWith("report:", StringComparison.Ordinal)));
    }

    [Fact]
    public void TouchDeleteObserversAndReporterFaultsAreContainedBeforeThrowingRoot()
    {
        var calls = new List<string>();
        var inner = new RecordingFactory();
        var factory = new SharedNativeHookFanoutFactory(
            inner,
            new ISharedNativeHookObserver[]
            {
                new ThrowingObserver("first", calls),
                new MarkerObserver("middle", calls),
                new ThrowingObserver("second", calls),
            },
            message =>
            {
                calls.Add($"report:{message}");
                throw new ApplicationException("reporter fault");
            });
        var expected = new InvalidOperationException("root-touch-delete");
        TouchTopMenuDeletingDestructorDelegate root = (_, _) =>
        {
            calls.Add("root");
            throw expected;
        };

        _ = factory.CreateHook(HookId.TouchTopMenuDeletingDestructor, root, 1);
        var actual = Assert.Throws<InvalidOperationException>(() =>
            inner.GetDetour<TouchTopMenuDeletingDestructorDelegate>(
                HookId.TouchTopMenuDeletingDestructor)(1, 2));

        Assert.Same(expected, actual);
        Assert.Collection(
            calls,
            item => Assert.Equal("first-touch-delete", item),
            item => Assert.Contains("InvalidOperationException: first-touch-delete-fault", item),
            item => Assert.Equal("middle-touch-delete", item),
            item => Assert.Equal("second-touch-delete", item),
            item => Assert.Contains("InvalidOperationException: second-touch-delete-fault", item),
            item => Assert.Equal("root", item));
        Assert.Equal(2, calls.Count(item => item.StartsWith("report:", StringComparison.Ordinal)));
    }

    [Fact]
    public void SequenceConstructorEnumeratesExactlyOnceAndKeepsImmutableOrderedDuplicates()
    {
        var calls = new List<string>();
        var duplicate = new MarkerObserver("duplicate", calls);
        var tail = new MarkerObserver("tail", calls);
        var source = new List<ISharedNativeHookObserver> { duplicate, duplicate, tail };
        var countedSource = new CountingEnumerable<ISharedNativeHookObserver>(source);
        var inner = new RecordingFactory();
        var factory = CreateFactory(inner, calls, countedSource);
        Assert.Equal(1, countedSource.EnumerationCount);
        source.Clear();
        NsMenuFocusSetterDelegate root = (_, _) => calls.Add("root");

        _ = factory.CreateHook(HookId.NsMenuFocusSetter, root, 0x5DD3E0);
        inner.GetDetour<NsMenuFocusSetterDelegate>(HookId.NsMenuFocusSetter)(0x10, 3);

        Assert.Equal(1, countedSource.EnumerationCount);
        Assert.Equal(["root", "duplicate-focus", "duplicate-focus", "tail-focus"], calls);
    }

    [Fact]
    public void ConstructorsRejectNullInputsAndObserverElements()
    {
        var inner = new RecordingFactory();
        Assert.Throws<ArgumentNullException>(() => new SharedNativeHookFanoutFactory(null!, new MarkerObserver("x", []), _ => { }));
        Assert.Throws<ArgumentNullException>(() => new SharedNativeHookFanoutFactory(inner, (ISharedNativeHookObserver)null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => new SharedNativeHookFanoutFactory(inner, (IEnumerable<ISharedNativeHookObserver>)null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => new SharedNativeHookFanoutFactory(inner, new ISharedNativeHookObserver[] { null! }, _ => { }));
        Assert.Throws<ArgumentNullException>(() => new SharedNativeHookFanoutFactory(inner, new MarkerObserver("x", []), null!));
    }

    [Fact]
    public void WrongSharedDelegateTypesAreRejectedBeforeInnerFactoryAndNonSharedPassesThrough()
    {
        var inner = new RecordingFactory();
        var factory = CreateFactory(inner, [], new MarkerObserver("observer", []));
        ModeSelectSteamInitDelegate wrong = _ => 1;

        Assert.Throws<InvalidOperationException>(() => factory.CreateHook(HookId.TextManagerGetMsg, wrong, 1));
        Assert.Throws<InvalidOperationException>(() => factory.CreateHook(HookId.NsMenuFocusSetter, wrong, 1));
        Assert.Throws<InvalidOperationException>(() => factory.CreateHook(HookId.NsMenuCustomButtonConstructor, wrong, 1));
        Assert.Throws<InvalidOperationException>(() => factory.CreateHook(HookId.NsMenuControlBinder, wrong, 1));
        Assert.Throws<InvalidOperationException>(() => factory.CreateHook(HookId.TouchTopMenuDeletingDestructor, wrong, 1));
        Assert.Empty(inner.Created);

        _ = factory.CreateHook(HookId.ModeSelectSteamInit, wrong, 0x6A9C60);
        Assert.Same(wrong, inner.GetDetour<ModeSelectSteamInitDelegate>(HookId.ModeSelectSteamInit));
    }

    [Fact]
    public void SingleObserverConstructorRemainsCompatible()
    {
        var calls = new List<string>();
        var inner = new RecordingFactory();
        var factory = new SharedNativeHookFanoutFactory(inner, new MarkerObserver("single", calls), _ => { });
        NsMenuFocusSetterDelegate root = (_, _) => calls.Add("root");

        _ = factory.CreateHook(HookId.NsMenuFocusSetter, root, 0x5DD3E0);
        inner.GetDetour<NsMenuFocusSetterDelegate>(HookId.NsMenuFocusSetter)(0x10, 3);

        Assert.Equal(["root", "single-focus"], calls);
    }

    [Fact]
    public void DeferredConfigurationSupportsCompositionCycleAndFreezesObserversExactlyOnce()
    {
        var calls = new List<string>();
        var inner = new RecordingFactory();
        var factory = new SharedNativeHookFanoutFactory(inner, message => calls.Add($"report:{message}"));
        NsMenuFocusSetterDelegate root = (_, _) => calls.Add("root");

        Assert.Throws<InvalidOperationException>(() =>
            factory.CreateHook(HookId.NsMenuFocusSetter, root, 0x5DD3E0));
        Assert.Empty(inner.Created);

        var first = new MarkerObserver("first", calls);
        var duplicate = new MarkerObserver("duplicate", calls);
        var source = new List<ISharedNativeHookObserver> { first, duplicate, duplicate };
        factory.ConfigureObservers(source);
        source.Clear();

        _ = factory.CreateHook(HookId.NsMenuFocusSetter, root, 0x5DD3E0);
        inner.GetDetour<NsMenuFocusSetterDelegate>(HookId.NsMenuFocusSetter)(0x10, 3);

        Assert.Equal(["root", "first-focus", "duplicate-focus", "duplicate-focus"], calls);
        Assert.Throws<InvalidOperationException>(() =>
            factory.ConfigureObservers([new MarkerObserver("late", calls)]));
    }

    [Fact]
    public void DeferredConfigurationRejectsNullSequenceAndElements()
    {
        var inner = new RecordingFactory();
        var nullSequence = new SharedNativeHookFanoutFactory(inner, _ => { });
        var nullElement = new SharedNativeHookFanoutFactory(inner, _ => { });

        Assert.Throws<ArgumentNullException>(() => nullSequence.ConfigureObservers(null!));
        Assert.Throws<ArgumentNullException>(() =>
            nullElement.ConfigureObservers(new ISharedNativeHookObserver[] { null! }));
    }

    [Fact]
    public void ReturnedWrapperRootsFanoutAndDelegatesOriginalReverseWrapperMetadataAndLifecycle()
    {
        var inner = new RecordingFactory(retainDetours: false);
        var factory = CreateFactory(inner, [], new MarkerObserver("observer", []));
        TextManagerGetMsgDelegate root = (_, result, _, _) => result;

        var hook = factory.CreateHook(HookId.TextManagerGetMsg, root, 0x5B9110);
        var nativeHook = inner.GetCreatedHook<TextManagerGetMsgDelegate>();
        var weak = inner.LastDetourReference!;
        ForceCollection();

        Assert.True(weak.IsAlive);
        Assert.Same(nativeHook.OriginalFunction, hook.OriginalFunction);
        Assert.Same(nativeHook.ReverseWrapper, hook.ReverseWrapper);
        Assert.Equal(nativeHook.OriginalFunctionAddress, hook.OriginalFunctionAddress);
        Assert.Equal(nativeHook.OriginalFunctionWrapperAddress, hook.OriginalFunctionWrapperAddress);
        Assert.Same(hook, hook.Activate());
        Assert.Equal(1, nativeHook.ActivateCalls);
        Assert.True(hook.IsHookActivated);
        hook.Disable();
        Assert.Equal(1, nativeHook.DisableCalls);
        Assert.False(hook.IsHookEnabled);
        hook.Enable();
        Assert.Equal(1, nativeHook.EnableCalls);
        Assert.True(hook.IsHookEnabled);
        GC.KeepAlive(hook);
    }

    private static SharedNativeHookFanoutFactory CreateFactory(
        RecordingFactory inner,
        List<string> calls,
        params ISharedNativeHookObserver[] observers) =>
        new(inner, observers, message => calls.Add($"report:{message}"));

    private static SharedNativeHookFanoutFactory CreateFactory(
        RecordingFactory inner,
        List<string> calls,
        IEnumerable<ISharedNativeHookObserver> observers) =>
        new(inner, observers, message => calls.Add($"report:{message}"));

    private static InvalidOperationException CreateAndInvokeThrowingRoot(
        HookId id,
        SharedNativeHookFanoutFactory factory,
        RecordingFactory inner,
        InvalidOperationException expected)
    {
        return id switch
        {
            HookId.TextManagerGetMsg => InvokeThrowingText(factory, inner, expected),
            HookId.NsMenuFocusSetter => InvokeThrowingFocus(factory, inner, expected),
            HookId.NsMenuCustomButtonConstructor => InvokeThrowingCustom(factory, inner, expected),
            HookId.NsMenuControlBinder => InvokeThrowingBinder(factory, inner, expected),
            HookId.TouchTopMenuDeletingDestructor => InvokeThrowingTouchDelete(factory, inner, expected),
            _ => throw new ArgumentOutOfRangeException(nameof(id)),
        };
    }

    private static InvalidOperationException InvokeThrowingText(
        SharedNativeHookFanoutFactory factory, RecordingFactory inner, InvalidOperationException expected)
    {
        TextManagerGetMsgDelegate root = (_, _, _, _) => throw expected;
        _ = factory.CreateHook(HookId.TextManagerGetMsg, root, 1);
        return Assert.Throws<InvalidOperationException>(() =>
            inner.GetDetour<TextManagerGetMsgDelegate>(HookId.TextManagerGetMsg)(1, 2, 3, 4));
    }

    private static InvalidOperationException InvokeThrowingFocus(
        SharedNativeHookFanoutFactory factory, RecordingFactory inner, InvalidOperationException expected)
    {
        NsMenuFocusSetterDelegate root = (_, _) => throw expected;
        _ = factory.CreateHook(HookId.NsMenuFocusSetter, root, 1);
        return Assert.Throws<InvalidOperationException>(() =>
            inner.GetDetour<NsMenuFocusSetterDelegate>(HookId.NsMenuFocusSetter)(1, 2));
    }

    private static InvalidOperationException InvokeThrowingCustom(
        SharedNativeHookFanoutFactory factory, RecordingFactory inner, InvalidOperationException expected)
    {
        NsMenuCustomButtonConstructorDelegate root = _ => throw expected;
        _ = factory.CreateHook(HookId.NsMenuCustomButtonConstructor, root, 1);
        return Assert.Throws<InvalidOperationException>(() =>
            inner.GetDetour<NsMenuCustomButtonConstructorDelegate>(HookId.NsMenuCustomButtonConstructor)(1));
    }

    private static InvalidOperationException InvokeThrowingBinder(
        SharedNativeHookFanoutFactory factory, RecordingFactory inner, InvalidOperationException expected)
    {
        NsMenuControlBinderDelegate root = (_, _, _) => throw expected;
        _ = factory.CreateHook(HookId.NsMenuControlBinder, root, 1);
        return Assert.Throws<InvalidOperationException>(() =>
            inner.GetDetour<NsMenuControlBinderDelegate>(HookId.NsMenuControlBinder)(1, 2, 3));
    }

    private static InvalidOperationException InvokeThrowingTouchDelete(
        SharedNativeHookFanoutFactory factory, RecordingFactory inner, InvalidOperationException expected)
    {
        TouchTopMenuDeletingDestructorDelegate root = (_, _) => throw expected;
        _ = factory.CreateHook(HookId.TouchTopMenuDeletingDestructor, root, 1);
        return Assert.Throws<InvalidOperationException>(() =>
            inner.GetDetour<TouchTopMenuDeletingDestructorDelegate>(HookId.TouchTopMenuDeletingDestructor)(1, 2));
    }

    private static void CreateAndInvokeSuccessfulRoot(
        HookId id,
        SharedNativeHookFanoutFactory factory,
        RecordingFactory inner,
        List<string> calls)
    {
        switch (id)
        {
            case HookId.TextManagerGetMsg:
                TextManagerGetMsgDelegate text = (_, _, _, _) => { calls.Add("root"); return 0x55; };
                _ = factory.CreateHook(id, text, 1);
                Assert.Equal((nint)0x55, inner.GetDetour<TextManagerGetMsgDelegate>(id)(1, 2, 3, 4));
                break;
            case HookId.NsMenuFocusSetter:
                NsMenuFocusSetterDelegate focus = (_, _) => calls.Add("root");
                _ = factory.CreateHook(id, focus, 1);
                inner.GetDetour<NsMenuFocusSetterDelegate>(id)(1, 2);
                break;
            case HookId.NsMenuCustomButtonConstructor:
                NsMenuCustomButtonConstructorDelegate custom = _ => { calls.Add("root"); return 0x66; };
                _ = factory.CreateHook(id, custom, 1);
                Assert.Equal((nint)0x66, inner.GetDetour<NsMenuCustomButtonConstructorDelegate>(id)(1));
                break;
            case HookId.NsMenuControlBinder:
                NsMenuControlBinderDelegate binder = (_, _, _) => calls.Add("root");
                _ = factory.CreateHook(id, binder, 1);
                inner.GetDetour<NsMenuControlBinderDelegate>(id)(1, 2, 3);
                break;
            case HookId.TouchTopMenuDeletingDestructor:
                TouchTopMenuDeletingDestructorDelegate touchDelete = (_, _) =>
                {
                    calls.Add("root");
                    return 0x77;
                };
                _ = factory.CreateHook(id, touchDelete, 1);
                Assert.Equal((nint)0x77, inner.GetDetour<TouchTopMenuDeletingDestructorDelegate>(id)(1, 2));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(id));
        }
    }

    private static void ForceCollection()
    {
        for (var index = 0; index < 3; index++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private sealed record TextPayload(nint TextManager, nint Result, int FileId, int MessageId, nint Returned);
    private sealed record FocusPayload(nint Manager, int ManagerKey);
    private sealed record CustomButtonPayload(nint Storage, nint Returned);
    private sealed record BinderPayload(nint Manager, nint FocusableState, int ManagerKey);
    private sealed record DeletingDestructorPayload(nint Node, uint DeletingFlags);

    private sealed class PayloadObserver(string name, List<string> calls) : ISharedNativeHookObserver
    {
        public List<TextPayload> TextPayloads { get; } = [];
        public List<FocusPayload> FocusPayloads { get; } = [];
        public List<CustomButtonPayload> CustomButtonPayloads { get; } = [];
        public List<BinderPayload> BinderPayloads { get; } = [];
        public List<DeletingDestructorPayload> DeletingDestructorPayloads { get; } = [];

        public void AfterTextManagerGetMsg(nint textManager, nint result, int fileId, int messageId, nint returned)
        {
            calls.Add($"{name}-text");
            TextPayloads.Add(new(textManager, result, fileId, messageId, returned));
        }

        public void AfterFocusSet(nint manager, int managerKey)
        {
            calls.Add($"{name}-focus");
            FocusPayloads.Add(new(manager, managerKey));
        }

        public void AfterCustomButtonConstructed(nint storage, nint returned)
        {
            calls.Add($"{name}-custom");
            CustomButtonPayloads.Add(new(storage, returned));
        }

        public void AfterControlBound(nint manager, nint focusableState, int managerKey)
        {
            calls.Add($"{name}-binder");
            BinderPayloads.Add(new(manager, focusableState, managerKey));
        }

        public void BeforeTouchTopMenuDeletingDestructor(nint node, uint deletingFlags)
        {
            calls.Add($"{name}-touch-delete");
            DeletingDestructorPayloads.Add(new(node, deletingFlags));
        }
    }

    private sealed class MarkerObserver(string name, List<string> calls) : ISharedNativeHookObserver
    {
        public void AfterTextManagerGetMsg(nint textManager, nint result, int fileId, int messageId, nint returned) =>
            calls.Add($"{name}-text");
        public void AfterFocusSet(nint manager, int managerKey) => calls.Add($"{name}-focus");
        public void AfterCustomButtonConstructed(nint storage, nint returned) => calls.Add($"{name}-custom");
        public void AfterControlBound(nint manager, nint focusableState, int managerKey) => calls.Add($"{name}-binder");
        public void BeforeTouchTopMenuDeletingDestructor(nint node, uint deletingFlags) => calls.Add($"{name}-touch-delete");
    }

    private sealed class ThrowingObserver(string name, List<string> calls) : ISharedNativeHookObserver
    {
        public void AfterTextManagerGetMsg(nint textManager, nint result, int fileId, int messageId, nint returned) =>
            Throw("text");
        public void AfterFocusSet(nint manager, int managerKey) => Throw("focus");
        public void AfterCustomButtonConstructed(nint storage, nint returned) => Throw("custom");
        public void AfterControlBound(nint manager, nint focusableState, int managerKey) => Throw("binder");
        public void BeforeTouchTopMenuDeletingDestructor(nint node, uint deletingFlags) => Throw("touch-delete");

        private void Throw(string shape)
        {
            calls.Add($"{name}-{shape}");
            throw new InvalidOperationException($"{name}-{shape}-fault");
        }
    }

    private sealed class CountingEnumerable<T>(IEnumerable<T> source) : IEnumerable<T>
    {
        public int EnumerationCount { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            EnumerationCount++;
            return source.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class RecordingFactory(bool retainDetours = true) : IRuntimeNativeHookFactory
    {
        private readonly Dictionary<HookId, Delegate> originals = new()
        {
            [HookId.TextManagerGetMsg] = (TextManagerGetMsgDelegate)((_, result, _, _) => result),
            [HookId.NsMenuFocusSetter] = (NsMenuFocusSetterDelegate)((_, _) => { }),
            [HookId.NsMenuCustomButtonConstructor] = (NsMenuCustomButtonConstructorDelegate)(storage => storage),
            [HookId.NsMenuControlBinder] = (NsMenuControlBinderDelegate)((_, _, _) => { }),
            [HookId.TouchTopMenuDeletingDestructor] =
                (TouchTopMenuDeletingDestructorDelegate)((node, _) => node),
            [HookId.ModeSelectSteamInit] = (ModeSelectSteamInitDelegate)(_ => 1),
        };
        private readonly Dictionary<HookId, Delegate> detours = [];
        private object? lastHook;

        public List<(HookId Id, nuint Address)> Created { get; } = [];
        public WeakReference? LastDetourReference { get; private set; }
        public TDelegate GetDetour<TDelegate>(HookId id) where TDelegate : Delegate => (TDelegate)detours[id];
        public FakeHook<TDelegate> GetCreatedHook<TDelegate>() where TDelegate : Delegate =>
            Assert.IsType<FakeHook<TDelegate>>(lastHook);

        public IHook<TDelegate> CreateHook<TDelegate>(HookId id, TDelegate detour, nuint address) where TDelegate : Delegate
        {
            Created.Add((id, address));
            LastDetourReference = new WeakReference(detour);
            if (retainDetours) detours[id] = detour;
            var hook = new FakeHook<TDelegate>((TDelegate)originals[id]);
            lastHook = hook;
            return hook;
        }
    }

    private sealed class FakeHook<TDelegate>(TDelegate original) : IHook<TDelegate> where TDelegate : Delegate
    {
        public TDelegate OriginalFunction { get; } = original;
        public IReverseWrapper<TDelegate> ReverseWrapper { get; } = new FakeReverseWrapper<TDelegate>(original);
        public bool IsHookEnabled { get; private set; }
        public bool IsHookActivated { get; private set; }
        public nint OriginalFunctionAddress => 0x111;
        public nint OriginalFunctionWrapperAddress => 0x222;
        public int ActivateCalls { get; private set; }
        public int DisableCalls { get; private set; }
        public int EnableCalls { get; private set; }

        public IHook<TDelegate> Activate()
        {
            ActivateCalls++;
            IsHookEnabled = true;
            IsHookActivated = true;
            return this;
        }

        IHook IHook.Activate() => Activate();
        public void Disable() { DisableCalls++; IsHookEnabled = false; }
        public void Enable() { EnableCalls++; IsHookEnabled = true; }
    }

    private sealed class FakeReverseWrapper<TDelegate>(TDelegate function) : IReverseWrapper<TDelegate>
        where TDelegate : Delegate
    {
        public TDelegate CSharpFunction { get; } = function;
        public nint NativeFunctionPtr => 0x333;
        public nint WrapperPointer => 0x444;
    }
}

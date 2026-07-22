using System.Collections.Immutable;
using Reloaded.Hooks.Definitions.X86;

namespace ChronoTriggerAccessibility.Native.Hooks;

public enum HookId
{
    TextManagerGetMsg,
    OpeTextResolver,
    SceneManagerCreate,
    SceneManagerNextScene,
    ModeSelectSteamInit,
    OpeManualSceneInit,
    NameInputSceneInit,
    NameInputSceneUpdate,
    NameConfirmationBuilder,
    TitleMenuModeEnter,
    TitleRowFactory,
    TitleSceneUpdate,
    TitleMenuCallback,
    NsMenuFocusSetter,
    NsMenuCustomButtonConstructor,
    NsMenuControlBinder,
    ModeSelectCallback,
    ControlNextCallback,
    NameActionCallback,
    NameDirectEntryActivation,
    NameDirectEntryClose,
    GallerySceneSwitchNode,
    ExtrasHubOnEnter,
    EndingLogOnEnter,
    EndingDetailOnEnter,
    ExtrasNodeOnExit,
    ExtrasHubDeletingDestructor,
    EndingLogDeletingDestructor,
    ExtrasHubCallback,
    EndingLogCallback,
    EndingDetailCallback,
    ExtrasLogTransition,
    ExtrasDetailTransition,
    MenuNodeConfigSteamConstructor,
    MenuNodeConfigSteamBuilder,
    MenuNodeConfigSteamDestructor,
    SettingsValueMutation,
    MsgWindowOpen,
    MsgWindowUpdate,
    MsgWindowClose,
    MsgWindowChoiceConfirmCallSite,
    ClassicTopMenuBuilder,
    TouchTopMenuBuilder,
    MenuTextLabelFactory,
    StatusBarFormatScope,
    StatusBarGlyphRenderer,
    StatusBarDestructor,
    ClassicTopMenuDeletingDestructor,
    TouchTopMenuDeletingDestructor,
    ClassicTopMenuActionDispatcher,
    TouchTopMenuActionDispatcher,
    ClassicTopMenuTimeLabelCallSite,
    ClassicTopMenuCurrencyLabelCallSite,
    ClassicTopMenuCaptionLabelCallSite,
    ClassicTopMenuMemberNameLabelCallSite,
    ClassicStatusRowLabelCallSite,
    ClassicStatusRowZeroValueCallSite,
    ClassicStatusUnavailableValueCallSite,
    ClassicStatusCurrentValueCallSite,
    ClassicStatusMaximumValueCallSite,
    ClassicStatusExtraLabelCallSite,
    TouchTopMenuTimeLabelCallSite,
    TouchTopMenuCurrencyLabelCallSite,
    TouchTopMenuCaptionLabelCallSite,
    TouchTopMenuMemberNameLabelCallSite,
    TouchTopMenuReserveNameLabelCallSite,
    CompactStatusRowLabelCallSite,
    CompactStatusRowZeroValueCallSite,
    CompactStatusCurrentValueCallSite,
    CompactStatusMaximumValueCallSite,
    CompactStatusExtraLabelCallSite,
    StatusBarGlyphRendererCallSite,
}

public enum X86CallingConvention
{
    MicrosoftThiscall,
    MicrosoftFastcall,
}

public enum NativeHookKind
{
    FunctionEntry,
    AssemblyCallSite,
}

public sealed class HookContract
{
    public HookContract(
        HookId id,
        string symbol,
        uint rva,
        ReadOnlySpan<byte> expectedBytes,
        Type delegateType,
        X86CallingConvention callingConvention)
        : this(
            id,
            symbol,
            rva,
            expectedBytes,
            NativeHookKind.FunctionEntry,
            delegateType,
            callingConvention)
    {
    }

    public HookContract(
        HookId id,
        string symbol,
        uint rva,
        ReadOnlySpan<byte> expectedBytes,
        NativeHookKind kind,
        Type? delegateType,
        X86CallingConvention? callingConvention)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported native hook kind.");
        }
        if (expectedBytes.IsEmpty)
        {
            throw new ArgumentException("A hook contract must include at least one expected byte.", nameof(expectedBytes));
        }

        if (kind == NativeHookKind.FunctionEntry)
        {
            if (delegateType is null || callingConvention is null || !Enum.IsDefined(callingConvention.Value))
            {
                throw new ArgumentException(
                    "A function-entry hook requires both a delegate type and one supported x86 calling convention.");
            }
            if (!typeof(Delegate).IsAssignableFrom(delegateType))
            {
                throw new ArgumentException("Hook delegate type must derive from System.Delegate.", nameof(delegateType));
            }
        }
        else if (delegateType is not null || callingConvention is not null)
        {
            throw new ArgumentException(
                "An assembly call-site contract is not callable and cannot declare a delegate or function convention.");
        }

        Id = id;
        Symbol = symbol;
        Rva = rva;
        ExpectedBytes = ImmutableArray.Create(expectedBytes.ToArray());
        Kind = kind;
        DelegateType = delegateType;
        CallingConvention = callingConvention;
    }

    public HookId Id { get; }

    public string Symbol { get; }

    public uint Rva { get; }

    public ImmutableArray<byte> ExpectedBytes { get; }

    public NativeHookKind Kind { get; }

    public Type? DelegateType { get; }

    public X86CallingConvention? CallingConvention { get; }
}

[Function(CallingConventions.MicrosoftThiscall)]
public delegate nint TextManagerGetMsgDelegate(nint textManager, nint result, int fileId, int messageId);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate nint OpeTextResolverDelegate(nint resolver, nint result, int bank, int messageId);

[Function(CallingConventions.Fastcall)]
public delegate nint SceneManagerCreateDelegate(int sceneId, int argument);

[Function(CallingConventions.Fastcall)]
public delegate void SceneManagerNextSceneDelegate(uint action);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate byte ModeSelectSteamInitDelegate(nint scene);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate byte OpeManualSceneInitDelegate(nint scene);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate byte NameInputSceneInitDelegate(nint scene);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void NameInputSceneUpdateDelegate(nint scene, float deltaSeconds);

// The caller places one 24-byte x86 MSVC std::string object on the stack by value.
// Six words preserve that exact stack layout and the native callee's ret 0x18 cleanup.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate void NameConfirmationBuilderDelegate(
    nint scene,
    uint stringWord0,
    uint stringWord1,
    uint stringWord2,
    uint stringWord3,
    uint stringLength,
    uint stringCapacity);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void TitleMenuModeEnterDelegate(nint mode);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate nint TitleRowFactoryDelegate(nint labelRecord);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void TitleSceneUpdateDelegate(nint scene, float deltaSeconds);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void TitleMenuCallbackDelegate(nint closure, nint eventTypePointer, nint rowIndexPointer);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void NsMenuFocusSetterDelegate(nint manager, int newIndex);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate nint NsMenuCustomButtonConstructorDelegate(nint storage);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void NsMenuControlBinderDelegate(nint manager, nint focusableState, int managerKey);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void ModeSelectCallbackDelegate(nint closure, int eventType, int controlOrDirection);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void ControlNextCallbackDelegate(nint closure, nint eventTypePointer, nint valuePointer);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void NameActionCallbackDelegate(nint closure, int eventType, int actionId);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void NameDirectEntryActivationDelegate(nint capture);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void NameDirectEntryCloseDelegate(nint capture);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate nint GallerySceneSwitchNodeDelegate(nint galleryScene, int action, uint rawStackWord1);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void ExtrasHubOnEnterDelegate(nint node);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void EndingLogOnEnterDelegate(nint node);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void EndingDetailOnEnterDelegate(nint node);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void ExtrasNodeOnExitDelegate(nint node);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate nint ExtrasHubDeletingDestructorDelegate(nint node, uint deletingFlags);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate nint EndingLogDeletingDestructorDelegate(nint node, uint deletingFlags);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void ExtrasHubCallbackDelegate(nint closure, int eventType, int action);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void EndingLogCallbackDelegate(nint closure, int eventType, int action);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void EndingDetailCallbackDelegate(nint closure, int eventType, int action);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void ExtrasLogTransitionDelegate(nint payload);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void ExtrasDetailTransitionDelegate(nint payload);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate nint MenuNodeConfigSteamConstructorDelegate(nint instance, int context);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void MenuNodeConfigSteamBuilderDelegate(nint instance);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void MenuNodeConfigSteamDestructorDelegate(nint instance);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate byte SettingsValueMutationDelegate(nint config, int page, int row, int proposedIndex);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void MsgWindowOpenDelegate(nint msgWindow, uint rawStackWord);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void MsgWindowUpdateDelegate(nint msgWindow, uint deltaSecondsBits);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void MsgWindowCloseDelegate(nint msgWindow, uint dummyStackWord);

[Function(CallingConventions.Cdecl)]
public delegate void MsgWindowChoiceConfirmProbeDelegate(nint msgWindow);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void ClassicTopMenuBuilderDelegate(nint topMenu, uint rawStackWord);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void TouchTopMenuBuilderDelegate(nint topMenu, uint rawStackWord);

[Function(CallingConventions.Fastcall)]
public delegate nint MenuTextLabelFactoryDelegate(nint position, nint msvcUtf8String, nint anchor, int fontSize);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void StatusBarFormatScopeDelegate(nint statusBar, nint msvcUtf8String, uint rawMode);

[Function(CallingConventions.Fastcall)]
public delegate nint StatusBarGlyphRendererDelegate(nint glyphOutput, nint msvcUtf16String, nint outputArgument);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void StatusBarDestructorDelegate(nint statusBar);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate nint ClassicTopMenuDeletingDestructorDelegate(nint topMenu, uint deletingFlags);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate nint TouchTopMenuDeletingDestructorDelegate(nint topOrEndingDetail, uint deletingFlags);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void TopMenuActionDispatcherDelegate(nint context);

[Function(CallingConventions.Cdecl)]
public delegate void NativeCallSiteProbeDelegate();

// Callable wrapper for the audited std::function<int()>::_Do_call target. Reloaded
// supplies the x86 thiscall-to-managed wrapper; the target object is passed in ECX.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate int ModeValueGetterDelegate(nint target);

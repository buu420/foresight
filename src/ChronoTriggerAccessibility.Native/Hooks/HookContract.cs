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
}

public enum X86CallingConvention
{
    MicrosoftThiscall,
    MicrosoftFastcall,
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
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(delegateType);
        if (!typeof(Delegate).IsAssignableFrom(delegateType))
        {
            throw new ArgumentException("Hook delegate type must derive from System.Delegate.", nameof(delegateType));
        }

        if (expectedBytes.IsEmpty)
        {
            throw new ArgumentException("A hook contract must include at least one expected byte.", nameof(expectedBytes));
        }

        Id = id;
        Symbol = symbol;
        Rva = rva;
        ExpectedBytes = ImmutableArray.Create(expectedBytes.ToArray());
        DelegateType = delegateType;
        CallingConvention = callingConvention;
    }

    public HookId Id { get; }

    public string Symbol { get; }

    public uint Rva { get; }

    public ImmutableArray<byte> ExpectedBytes { get; }

    public Type DelegateType { get; }

    public X86CallingConvention CallingConvention { get; }
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

// Callable wrapper for the audited std::function<int()>::_Do_call target. Reloaded
// supplies the x86 thiscall-to-managed wrapper; the target object is passed in ECX.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate int ModeValueGetterDelegate(nint target);

using System.Collections.Immutable;
using Reloaded.Hooks.Definitions.X86;

namespace ChronoTriggerAccessibility.Native.Hooks;

public enum HookId
{
    TextManagerGetMsg,
    SceneManagerCreate,
    SceneManagerNextScene,
    ModeSelectSteamInit,
    OpeManualSceneInit,
    NameInputSceneUpdate,
    TitleMenuModeEnter,
    TitleRowFactory,
    TitleSceneUpdate,
    TitleMenuCallback,
    NsMenuFocusSetter,
    ModeSelectCallback,
    ControlNextCallback,
    NameActionCallback,
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

[Function(CallingConventions.Fastcall)]
public delegate nint SceneManagerCreateDelegate(int sceneId, int argument);

[Function(CallingConventions.Fastcall)]
public delegate void SceneManagerNextSceneDelegate(uint action);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate byte ModeSelectSteamInitDelegate(nint scene);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate byte OpeManualSceneInitDelegate(nint scene);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void NameInputSceneUpdateDelegate(nint scene, float deltaSeconds);

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
public delegate void ModeSelectCallbackDelegate(nint closure, int eventType, int controlOrDirection);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void ControlNextCallbackDelegate(nint closure, nint eventTypePointer);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void NameActionCallbackDelegate(nint closure, int eventType, int actionId);

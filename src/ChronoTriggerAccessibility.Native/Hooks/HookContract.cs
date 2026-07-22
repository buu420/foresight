using System.Collections.Immutable;

namespace ChronoTriggerAccessibility.Native.Hooks;

public enum HookId
{
    TextManagerGetMsg,
    SceneManagerCreate,
    ModeSelectSteamInit,
    OpeManualSceneInit,
    NameInputSceneUpdate,
    TitleMenuModeEnter,
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

public delegate nint TextManagerGetMsgDelegate(nint textManager, nint result, int fileId, int messageId);

public delegate nint SceneManagerCreateDelegate(int sceneId, int argument);

public delegate nint ModeSelectSteamInitDelegate(nint scene);

public delegate nint OpeManualSceneInitDelegate(nint scene);

public delegate void NameInputSceneUpdateDelegate(nint scene, float deltaSeconds);

public delegate nint TitleMenuModeEnterDelegate(nint mode);

public delegate void TitleSceneUpdateDelegate(nint scene, float deltaSeconds);

public delegate void TitleMenuCallbackDelegate(nint closure, nint eventTypePointer, nint rowIndexPointer);

public delegate void NsMenuFocusSetterDelegate(nint manager, int newIndex);

public delegate void ModeSelectCallbackDelegate(nint closure, int eventType, int controlOrDirection);

public delegate void ControlNextCallbackDelegate(nint closure, nint eventTypePointer);

public delegate void NameActionCallbackDelegate(nint closure, nint eventTypePointer, nint actionIdPointer);

using System.Collections.ObjectModel;
using System.Reflection.PortableExecutable;
using ChronoTriggerAccessibility.Native.Build;

namespace ChronoTriggerAccessibility.Native.Hooks;

public static class GameVersionCatalog
{
    public static ExecutableIdentity Executable { get; } = new(
        "8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7",
        Machine.I386,
        0x00400000);

    public static IReadOnlyList<HookContract> Hooks { get; } = new ReadOnlyCollection<HookContract>(
    [
        Create(HookId.TextManagerGetMsg, "TextManager::getMsg", 0x1B92D0,
            "55 8B EC 51 8B 45 0C FF 75 10 C7 45 FC 00 00 00 00",
            typeof(TextManagerGetMsgDelegate), X86CallingConvention.MicrosoftThiscall),
        Create(HookId.SceneManagerCreate, "SceneManager::create", 0x297860,
            "55 8B EC 6A FF 68 F8 73 76 00 64 A1 00 00 00 00",
            typeof(SceneManagerCreateDelegate), X86CallingConvention.MicrosoftFastcall),
        Create(HookId.ModeSelectSteamInit, "ModeSelectSteam::init", 0x2A9C60,
            "55 8B EC 6A FF 68 B2 AE 77 00 64 A1 00 00 00 00",
            typeof(ModeSelectSteamInitDelegate), X86CallingConvention.MicrosoftThiscall),
        Create(HookId.OpeManualSceneInit, "OpeManualScene::init", 0x2ADB50,
            "55 8B EC 6A FF 68 E4 B5 77 00 64 A1 00 00 00 00",
            typeof(OpeManualSceneInitDelegate), X86CallingConvention.MicrosoftThiscall),
        Create(HookId.NameInputSceneUpdate, "NameInputScene::update", 0x2C2C50,
            "55 8B EC 83 E4 F8 51 53 56 57 8B F9 80 BF 94 02",
            typeof(NameInputSceneUpdateDelegate), X86CallingConvention.MicrosoftThiscall),
        Create(HookId.TitleMenuModeEnter, "TitleMenuMode::enter", 0x2CF560,
            "55 8B EC 6A FF 68 84 E4 77 00 64 A1 00 00 00 00",
            typeof(TitleMenuModeEnterDelegate), X86CallingConvention.MicrosoftThiscall),
        Create(HookId.TitleSceneUpdate, "TitleScene::update", 0x2D1030,
            "55 8B EC F3 0F 10 45 08 56 8B F1 8B 8E 90 02 00",
            typeof(TitleSceneUpdateDelegate), X86CallingConvention.MicrosoftThiscall),
        Create(HookId.TitleMenuCallback, "TitleMenu callback", 0x2D12A0,
            "55 8B EC 8B 45 0C 56 57 8B F1 8B 38 8B 45 08 8B",
            typeof(TitleMenuCallbackDelegate), X86CallingConvention.MicrosoftThiscall),
        Create(HookId.NsMenuFocusSetter, "nsMenu focus setter", 0x1DD3E0,
            "55 8B EC 83 EC 20 8B C1 53 8B 5D 08 57 8D B8 C4 02 00 00 89 45 FC C6 80 CC 02 00 00 01 89 7D EC",
            typeof(NsMenuFocusSetterDelegate), X86CallingConvention.MicrosoftThiscall),
        Create(HookId.ModeSelectCallback, "ModeSelect callback", 0x2AB9E0,
            "55 8B EC 83 E4 F8 83 EC 14 8B 45 08 53 8B D9 89 5C 24 04 56 57 83 F8 03 0F 87 B2 02 00 00 FF 24",
            typeof(ModeSelectCallbackDelegate), X86CallingConvention.MicrosoftThiscall),
        Create(HookId.ControlNextCallback, "Control Next callback", 0x2AE840,
            "55 8B EC 83 E4 F8 51 8B 45 08 56 8B F1 8B 00 83 E8 00 74 05 83 E8 02 75 23 C7 05 CC C3 81 00 18",
            typeof(ControlNextCallbackDelegate), X86CallingConvention.MicrosoftThiscall),
        Create(HookId.NameActionCallback, "Name action callback", 0x2C1760,
            "55 8B EC 6A FF 68 FF D4 77 00 64 A1 00 00 00 00 50 83 EC 70 A1 D0 A0 7F 00 33 C5 89 45 F0 56 57",
            typeof(NameActionCallbackDelegate), X86CallingConvention.MicrosoftThiscall),
    ]);

    public static HookContract Get(HookId id) => Hooks.Single(hook => hook.Id == id);

    private static HookContract Create(
        HookId id,
        string symbol,
        uint rva,
        string expectedBytes,
        Type delegateType,
        X86CallingConvention callingConvention) =>
        new(id, symbol, rva, Convert.FromHexString(expectedBytes.Replace(" ", string.Empty, StringComparison.Ordinal)),
            delegateType, callingConvention);
}

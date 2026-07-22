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
        Create(HookId.TextManagerGetMsg, "TextManager::getMsg", 0x1B9110,
            "55 8B EC 6A FF 68 A1 AD 76 00 64 A1 00 00 00 00",
            typeof(TextManagerGetMsgDelegate), X86CallingConvention.MicrosoftThiscall),
        Create(HookId.OpeTextResolver, "Ope localized text resolver", 0x1B9060,
            "55 8B EC 51 8B 41 04 8B 11 2B C2 8B 4D 0C C1",
            typeof(OpeTextResolverDelegate), X86CallingConvention.MicrosoftThiscall),
        Create(HookId.SceneManagerCreate, "SceneManager::create", 0x297860,
            "55 8B EC 6A FF 68 F8 73 76 00 64 A1 00 00 00 00",
            typeof(SceneManagerCreateDelegate), X86CallingConvention.MicrosoftFastcall),
        Create(HookId.SceneManagerNextScene, "SceneManager::NextScene", 0x297B60,
            "55 8B EC 6A FF 68 E8 94 77 00 64 A1 00 00 00 00",
            typeof(SceneManagerNextSceneDelegate), X86CallingConvention.MicrosoftFastcall),
        Create(HookId.ModeSelectSteamInit, "ModeSelectSteam::init", 0x2A9C60,
            "55 8B EC 6A FF 68 B2 AE 77 00 64 A1 00 00 00 00",
            typeof(ModeSelectSteamInitDelegate), X86CallingConvention.MicrosoftThiscall),
        Create(HookId.OpeManualSceneInit, "OpeManualScene::init", 0x2ADB50,
            "55 8B EC 6A FF 68 E4 B5 77 00 64 A1 00 00 00 00",
            typeof(OpeManualSceneInitDelegate), X86CallingConvention.MicrosoftThiscall),
        Create(HookId.NameInputSceneInit, "NameInputScene::init", 0x2C0090,
            "55 8B EC 83 E4 F8 51 56 8B F1 FF 15 68 59 78 00",
            typeof(NameInputSceneInitDelegate), X86CallingConvention.MicrosoftThiscall),
        Create(HookId.NameInputSceneUpdate, "NameInputScene::update", 0x2C2C50,
            "55 8B EC 83 E4 F8 51 53 56 57 8B F9 80 BF 94 02",
            typeof(NameInputSceneUpdateDelegate), X86CallingConvention.MicrosoftThiscall),
        Create(HookId.NameConfirmationBuilder, "Name confirmation builder", 0x2C2F00,
            "55 8B EC 6A FF 68 70 D8 77 00 64 A1 00 00 00 00",
            typeof(NameConfirmationBuilderDelegate), X86CallingConvention.MicrosoftThiscall),
        Create(HookId.TitleMenuModeEnter, "TitleMenuMode::enter", 0x2CF560,
            "55 8B EC 6A FF 68 84 E4 77 00 64 A1 00 00 00 00",
            typeof(TitleMenuModeEnterDelegate), X86CallingConvention.MicrosoftThiscall),
        Create(HookId.TitleRowFactory, "Title row factory", 0x2CD7A0,
            "55 8B EC 6A FF 68 11 E2 77 00 64 A1 00 00 00 00",
            typeof(TitleRowFactoryDelegate), X86CallingConvention.MicrosoftThiscall),
        Create(HookId.TitleSceneUpdate, "TitleScene::update", 0x2D1030,
            "55 8B EC F3 0F 10 45 08 56 8B F1 8B 8E 90 02 00",
            typeof(TitleSceneUpdateDelegate), X86CallingConvention.MicrosoftThiscall),
        Create(HookId.TitleMenuCallback, "TitleMenu callback", 0x2D12A0,
            "55 8B EC 8B 45 0C 56 57 8B F1 8B 38 8B 45 08 8B",
            typeof(TitleMenuCallbackDelegate), X86CallingConvention.MicrosoftThiscall),
        Create(HookId.NsMenuFocusSetter, "nsMenu focus setter", 0x1DD3E0,
            "55 8B EC 83 EC 20 8B C1 53 8B 5D 08 57 8D B8 C4 02 00 00 89 45 FC C6 80 CC 02 00 00 01 89 7D EC",
            typeof(NsMenuFocusSetterDelegate), X86CallingConvention.MicrosoftThiscall),
        Create(HookId.NsMenuCustomButtonConstructor, "nsMenu CustomButton constructor", 0x1D2160,
            "55 8B EC 6A FF 68 10 44 76 00 64 A1 00 00 00 00",
            typeof(NsMenuCustomButtonConstructorDelegate), X86CallingConvention.MicrosoftThiscall),
        Create(HookId.NsMenuControlBinder, "nsMenu control binder", 0x1DD260,
            "55 8B EC 6A FF 68 DE E3 76 00 64 A1 00 00 00 00",
            typeof(NsMenuControlBinderDelegate), X86CallingConvention.MicrosoftThiscall),
        Create(HookId.ModeSelectCallback, "ModeSelect callback", 0x2AB9E0,
            "55 8B EC 83 E4 F8 83 EC 14 8B 45 08 53 8B D9 89 5C 24 04 56 57 83 F8 03 0F 87 B2 02 00 00 FF 24",
            typeof(ModeSelectCallbackDelegate), X86CallingConvention.MicrosoftThiscall),
        Create(HookId.ControlNextCallback, "Control Next callback", 0x2AE840,
            "55 8B EC 83 E4 F8 51 8B 45 08 56 8B F1 8B 00 83 E8 00 74 05 83 E8 02 75 23 C7 05 CC C3 81 00 18",
            typeof(ControlNextCallbackDelegate), X86CallingConvention.MicrosoftThiscall),
        Create(HookId.NameActionCallback, "Name action callback", 0x2C1760,
            "55 8B EC 6A FF 68 FF D4 77 00 64 A1 00 00 00 00 50 83 EC 70 A1 D0 A0 7F 00 33 C5 89 45 F0 56 57",
            typeof(NameActionCallbackDelegate), X86CallingConvention.MicrosoftThiscall),
        Create(HookId.NameDirectEntryActivation, "Name direct-entry activation", 0x2C1B50,
            "56 57 8B F9 6A 01 8B 07 C6 80 90 02 00 00 01 8B 4F 04 E8 29 DB FF FF 33 F6 0F 1F 80 00 00 00 00",
            typeof(NameDirectEntryActivationDelegate), X86CallingConvention.MicrosoftThiscall),
        Create(HookId.NameDirectEntryClose, "Name direct-entry close", 0x2C1BA0,
            "56 57 8B F9 33 F6 8B 07 C6 80 90 02 00 00 00 90 8B 47 04 6A 01 8B 0C 06 8B 01 FF 90 B8 02 00 00",
            typeof(NameDirectEntryCloseDelegate), X86CallingConvention.MicrosoftThiscall),
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

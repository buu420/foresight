using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Hooks;
using Reloaded.Hooks.Definitions.X86;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Hooks;

public sealed class GameVersionCatalogTests
{
    public static TheoryData<HookId, string, uint, string, Type, X86CallingConvention> ExpectedContracts => new()
    {
        { HookId.ClassicFieldMenuReplace, "Classic field submenu replacement", 0x2A5270, "558BEC56578BF98B8F9002000085C974128B01FF", typeof(SubmenuNodeWordDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.TouchFieldMenuReplace, "Touch field submenu replacement", 0x2BD050, "558BEC56578BF98B8F9402000085C974128B01FF", typeof(SubmenuNodeWordDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.SaveSlotOpen, "Steam save file list open", 0x218A20, "558BEC6AFF682E34770064A1000000005083EC74", typeof(SaveSlotOpenDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.MenuManagerDispatch, "Menu manager completed callback", 0x1DD4C0, "558BEC83B9BC0200000074298B450C8B89BC0200", typeof(SubmenuManagerDispatchDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.InventoryHelpRefresh, "Inventory selected item details", 0x1C7610, "558BEC6AFF68E0C1760064A1000000005083EC38", typeof(SubmenuNodeWordDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.SaveSlotDetailsRefresh, "Save file selected details", 0x218FE0, "558BEC6AFF688C3E770064A1000000005081ECD8", typeof(SubmenuNodeWordDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.MenuManagerUpdate, "Menu manager completed update", 0x1DCF10, "558BEC83E4F883EC14538BD9565780BB90020000", typeof(SubmenuNodeWordDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.FormationSceneInit, "FormationSteamScene::init", 0x2A49A0, "558BEC6AFF688F5C760064A100000000", typeof(FormationSceneInitDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.FormationSceneDestructor, "FormationSteamScene::deletingDestructor", 0x2A4960, "558BECA1C4B48100568BF1C706480A7B00", typeof(FormationSceneDestructorDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.TimeGaugeSceneInit, "AgeSelectScene::init", 0x2989B0, "558BEC6AFF68BF95770064A100000000", typeof(TimeGaugeSceneInitDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.TimeGaugeSceneUpdate, "AgeSelectScene::update", 0x2996D0, "558BEC83E4F851A1DCC3810085C05356", typeof(TimeGaugeSceneUpdateDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.TextManagerGetMsg, "TextManager::getMsg", 0x1B9110, "558BEC6AFF68A1AD760064A100000000", typeof(TextManagerGetMsgDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.OpeTextResolver, "Ope localized text resolver", 0x1B9060, "558BEC518B41048B112BC28B4D0CC1", typeof(OpeTextResolverDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.SceneManagerCreate, "SceneManager::create", 0x297860, "558BEC6AFF68F873760064A100000000", typeof(SceneManagerCreateDelegate), X86CallingConvention.MicrosoftFastcall },
        { HookId.SceneManagerNextScene, "SceneManager::NextScene", 0x297B60, "558BEC6AFF68E894770064A100000000", typeof(SceneManagerNextSceneDelegate), X86CallingConvention.MicrosoftFastcall },
        { HookId.ModeSelectSteamInit, "ModeSelectSteam::init", 0x2A9C60, "558BEC6AFF68B2AE770064A100000000", typeof(ModeSelectSteamInitDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.OpeManualSceneInit, "OpeManualScene::init", 0x2ADB50, "558BEC6AFF68E4B5770064A100000000", typeof(OpeManualSceneInitDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.NameInputSceneInit, "NameInputScene::init", 0x2C0090, "558BEC83E4F851568BF1FF1568597800", typeof(NameInputSceneInitDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.NameInputSceneUpdate, "NameInputScene::update", 0x2C2C50, "558BEC83E4F8515356578BF980BF9402", typeof(NameInputSceneUpdateDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.NameConfirmationBuilder, "Name confirmation builder", 0x2C2F00, "558BEC6AFF6870D8770064A100000000", typeof(NameConfirmationBuilderDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.TitleMenuModeEnter, "TitleMenuMode::enter", 0x2CF560, "558BEC6AFF6884E4770064A100000000", typeof(TitleMenuModeEnterDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.TitleRowFactory, "Title row factory", 0x2CD7A0, "558BEC6AFF6811E2770064A100000000", typeof(TitleRowFactoryDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.TitleSceneUpdate, "TitleScene::update", 0x2D1030, "558BECF30F104508568BF18B8E900200", typeof(TitleSceneUpdateDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.TitleMenuCallback, "TitleMenu callback", 0x2D12A0, "558BEC8B450C56578BF18B388B45088B", typeof(TitleMenuCallbackDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.NsMenuFocusSetter, "nsMenu focus setter", 0x1DD3E0, "558BEC83EC208BC1538B5D08578DB8C40200008945FCC680CC02000001897DEC", typeof(NsMenuFocusSetterDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.NsMenuCustomButtonConstructor, "nsMenu CustomButton constructor", 0x1D2160, "558BEC6AFF681044760064A100000000", typeof(NsMenuCustomButtonConstructorDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.NsMenuControlBinder, "nsMenu control binder", 0x1DD260, "558BEC6AFF68DEE3760064A100000000", typeof(NsMenuControlBinderDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.ModeSelectCallback, "ModeSelect callback", 0x2AB9E0, "558BEC83E4F883EC148B4508538BD9895C2404565783F8030F87B2020000FF24", typeof(ModeSelectCallbackDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.ControlNextCallback, "Control Next callback", 0x2AE840, "558BEC83E4F8518B4508568BF18B0083E800740583E8027523C705CCC3810018", typeof(ControlNextCallbackDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.NameActionCallback, "Name action callback", 0x2C1760, "558BEC6AFF68FFD4770064A1000000005083EC70A1D0A07F0033C58945F05657", typeof(NameActionCallbackDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.NameDirectEntryActivation, "Name direct-entry activation", 0x2C1B50, "56578BF96A018B07C68090020000018B4F04E829DBFFFF33F60F1F8000000000", typeof(NameDirectEntryActivationDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.NameDirectEntryClose, "Name direct-entry close", 0x2C1BA0, "56578BF933F68B07C6809002000000908B47046A018B0C068B01FF90B8020000", typeof(NameDirectEntryCloseDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.NameGridRefresh, "Name character-grid refresh", 0x2C2A20, "558BEC6AFF68B061770064A1000000005083EC40A1D0A07F0033C58945F0535657508D45F464A300000000894DBC33FF", typeof(NameGridRefreshDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.GallerySceneSwitchNode, "GalleryScene::switchNode", 0x2A52B0, "558BEC6AFF6838AB770064A1000000005083EC64A1D0A07F", typeof(GallerySceneSwitchNodeDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.ExtrasHubOnEnter, "Extras Hub onEnter", 0x1DB570, "568BF1FF15AC5A7800FFB6CC0200008BCEE80A0000005EC3", typeof(ExtrasHubOnEnterDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.EndingLogOnEnter, "Ending Log onEnter", 0x1D3980, "568BF1FF15AC5A7800FFB6C80200008BCEE80A0000005EC3", typeof(EndingLogOnEnterDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.EndingDetailOnEnter, "Ending Detail onEnter", 0x1D2730, "568BF1FF15AC5A7800518BCEE82F0000005EC3", typeof(EndingDetailOnEnterDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.ExtrasNodeOnExit, "Extras node onExit", 0x1D2750, "568BF1FF15A05A78008B8E900200005E8B01FFA048010000", typeof(ExtrasNodeOnExitDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.ExtrasHubDeletingDestructor, "Extras Hub deleting destructor", 0x1DB460, "558BEC568BF1E825000000F6450801740E680803000056E86972180083C4088BC65E5DC20400", typeof(ExtrasHubDeletingDestructorDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.EndingLogDeletingDestructor, "Ending Log deleting destructor", 0x1D38E0, "558BEC6AFF68C75B760064A100000000505657A1D0A07F0033C5508D45F464A3000000008BF1C70688637A008B8EC002000085C974068B016A01FF108DBE98020000C745FC000000008B4F2485C974158B113BCF0F95C00FB6C050FF5210C74724000000008BCEFF156C597800F6450801740E68E002000056E887ED180083C4088BC68B4DF464890D00000000595F5E8BE55DC20400", typeof(EndingLogDeletingDestructorDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.ExtrasHubCallback, "Extras hub callback", 0x1DC610, "558BEC83E4F88B450883EC08568BF15783E8000F849E0000", typeof(ExtrasHubCallbackDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.EndingLogCallback, "Ending Log callback", 0x1D4850, "558BEC8B4508568BF15783E8000F849200000083E8017435", typeof(EndingLogCallbackDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.EndingDetailCallback, "Ending Detail callback", 0x1D35A0, "558BEC8B4508568BF183E800745983E801744083E8010F85", typeof(EndingDetailCallbackDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.ExtrasLogTransition, "Extras Ending Log transition", 0x2A5E20, "558BEC6AFF6810AC770064A1000000005083EC5CA1D0A07F", typeof(ExtrasLogTransitionDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.ExtrasDetailTransition, "Extras Ending Detail transition", 0x2A60C0, "558BEC6AFF68CEAC770064A1000000005081EC80010000A1D0A07F0033C58945F0", typeof(ExtrasDetailTransitionDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.SaveLoadConfirmationBuilder, "MenuNodeSaveLoadSteam confirmation builder", 0x21A1D0, "558BEC6AFF68EC3F770064A1000000005081ECA0000000A1D0A07F0033C58945", typeof(SaveLoadConfirmationBuilderDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.SaveLoadNodeDestructor, "MenuNodeSaveLoadSteam destructor", 0x218860, "558BEC6AFF68C75B760064A100000000505657A1D0A07F0033C5508D45F464A3", typeof(SaveLoadNodeDestructorDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.MenuNodeConfigSteamConstructor, "MenuNodeConfigSteam constructor", 0x1ECB00, "558BEC6AFF681044760064A1000000005056A1D0A07F0033C5508D45F464A300", typeof(MenuNodeConfigSteamConstructorDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.MenuNodeConfigSteamBuilder, "MenuNodeConfigSteam builder", 0x1ED020, "558BEC6AFF684102770064A1000000005081EC640E0000A1D0A07F0033C58945", typeof(MenuNodeConfigSteamBuilderDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.MenuNodeConfigSteamDestructor, "MenuNodeConfigSteam destructor", 0x1EC970, "558BEC6AFF689EC6760064A100000000505657A1D0A07F0033C5508D45F464A3", typeof(MenuNodeConfigSteamDestructorDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.MenuNodeConfigSteamPageBuilder, "MenuNodeConfigSteam page builder", 0x1F0310, "558BEC6AFF680E06770064A1000000005081EC80030000A1D0A07F0033C58945EC535657", typeof(MenuNodeConfigSteamPageBuilderDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.SteamSettingsCategoryCallback, "Steam Settings category callback", 0x1F0000, "558BEC6AFF68B803770064A1000000005083EC30A1D0A07F0033C58945F05356", typeof(SteamSettingsCallbackDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.SteamSettingsRowCallback, "Steam Settings row callback", 0x1F21F0, "558BEC6AFF688006770064A1000000005083EC44A1D0A07F0033C58945F05356", typeof(SteamSettingsCallbackDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.SteamSettingsLicenseCallback, "Steam Settings license callback", 0x1F3960, "558BEC6AFF681A08770064A1000000005083EC44A1D0A07F0033C58945F05657", typeof(SteamSettingsCallbackDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.SteamSettingsSetterInvoker, "Steam Settings setter invoker", 0x1C0D80, "558BEC8B492485C97506FF15BC5078008B018D550852FF50085DC20400", typeof(SteamSettingsSetterInvokerDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.SteamSettingsResolutionBuilder, "Steam Settings resolution builder", 0x1FA010, "558BEC6AFF68E610770064A1000000005081EC48010000A1D0A07F0033C58945F0535657", typeof(SteamSettingsNestedBuilderDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.SteamSettingsResolutionCallback, "Steam Settings resolution callback", 0x1FB100, "558BEC6AFF682011770064A1000000005083EC185657A1D0A07F0033C5508D45F464A300", typeof(SteamSettingsCallbackDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.SteamSettingsConfirmationBuilderA, "Steam Settings confirmation A builder", 0x1F3CC0, "558BEC6AFF68FE08770064A1000000005081ECD4000000A1D0A07F0033C58945F0535657", typeof(SteamSettingsNestedBuilderDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.SteamSettingsConfirmationBuilderB, "Steam Settings confirmation B builder", 0x1F45D0, "558BEC6AFF68FE08770064A1000000005081ECD4000000A1D0A07F0033C58945F0535657", typeof(SteamSettingsNestedBuilderDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.MenuNodeConfigConstructor, "MenuNodeConfig constructor", 0x1DFAD0, "558BEC6AFF681044760064A1000000005056A1D0A07F0033C5508D45F464A3000000008B", typeof(MenuNodeConfigConstructorDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.MenuNodeConfigBuilder, "MenuNodeConfig builder", 0x1E0180, "558BEC6AFF6807ED760064A1000000005081EC680C0000A1D0A07F0033C58945F0535657", typeof(MenuNodeConfigBuilderDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.MenuNodeConfigDestructor, "MenuNodeConfig destructor", 0x1DF880, "558BEC6AFF68EEE6760064A10000000050515657A1D0A07F0033C5508D45F464A300000000", typeof(MenuNodeConfigDestructorDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.TouchSettingsPageTransition, "Touch Settings page transition", 0x1E1D00, "558BEC83E4F8515356578BD9C705CCC3810018008000E8D518FCFF8B038B90C00200008B", typeof(TouchSettingsPageTransitionDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.TouchSettingsCallback, "Touch Settings callback", 0x1E3530, "558BEC6AFF6848EF760064A1000000005083EC30A1D0A07F0033C58945F05356", typeof(TouchSettingsCallbackDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.TouchSettingsValueMutation, "Touch Settings value mutation", 0x1E3980, "558BEC538BD95669750C98000000", typeof(TouchSettingsValueMutationDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.TouchSettingsConfirmationBuilderA, "Touch Settings confirmation A builder", 0x1E4030, "558BEC6AFF687EF0760064A1000000005081ECD4000000A1D0A07F0033C58945F0535657", typeof(TouchSettingsNestedBuilderDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.TouchSettingsConfirmationBuilderB, "Touch Settings confirmation B builder", 0x1E4AB0, "558BEC6AFF68D2F1760064A1000000005081EC08010000A1D0A07F0033C58945F0535657", typeof(TouchSettingsNestedBuilderDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.TouchSettingsConfirmationCallbackA, "Touch Settings confirmation A callback", 0x1E4880, "558BEC6AFF68BFF0760064A1000000005083EC3CA1D0A07F0033C58945F05356", typeof(TouchSettingsCallbackDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.TouchSettingsConfirmationCallbackB, "Touch Settings confirmation B callback", 0x1E5380, "558BEC6AFF680FF2760064A1000000005083EC38A1D0A07F0033C58945F05356", typeof(TouchSettingsCallbackDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.MsgWindowOpen, "MsgWindow open/parser", 0x195B40, "558BEC6AFF68CF9C760064A1000000005083EC30A1D0A07F0033C58945EC5356", typeof(MsgWindowOpenDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.MsgWindowUpdate, "MsgWindow update", 0x197530, "558BEC6AFF68A89F760064A1000000005083EC54A1D0A07F0033C58945F05356", typeof(MsgWindowUpdateDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.MsgWindowClose, "MsgWindow close", 0x195C70, "558BEC6AFF68179D760064A1000000005083EC48A1D0A07F0033C58945EC5356", typeof(MsgWindowCloseDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.ClassicTopMenuBuilder, "Classic top-menu builder", 0x1D0560, "558BEC6AFF683ECE760064A1000000005081EC94010000A1D0A07F0033C58945", typeof(ClassicTopMenuBuilderDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.TouchTopMenuBuilder, "Touch/mouse top-menu builder", 0x221660, "558BEC6AFF681648770064A1000000005081ECEC000000A1D0A07F0033C58945", typeof(TouchTopMenuBuilderDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.MenuTextLabelFactory, "Menu UTF-8 text-label factory", 0x2400B0, "558BEC83E4F883EC0C8BC28B550C53568BD98BC857E8C6F9DCFF6A0083EC088D", typeof(MenuTextLabelFactoryDelegate), X86CallingConvention.MicrosoftFastcall },
        { HookId.StatusBarFormatScope, "StatusBar text-format scope", 0x22F160, "558BEC6AFF682F5D770064A1000000005081EC24010000A1D0A07F0033C58945", typeof(StatusBarFormatScopeDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.StatusBarGlyphRenderer, "StatusBar UTF-16 glyph renderer", 0x22E080, "558BEC6AFF68C1C5760064A1000000005083EC58A1D0A07F0033C58945EC5356", typeof(StatusBarGlyphRendererDelegate), X86CallingConvention.MicrosoftFastcall },
        { HookId.StatusBarDestructor, "StatusBar destructor", 0x22E650, "558BEC6AFF68EEE6760064A10000000050515657A1D0A07F0033C5508D45F464", typeof(StatusBarDestructorDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.ClassicTopMenuDeletingDestructor, "Classic top-menu deleting destructor", 0x1D0470, "558BEC6AFF68C75B760064A100000000505657A1D0A07F0033C5508D45F464A3", typeof(ClassicTopMenuDeletingDestructorDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.TouchTopMenuDeletingDestructor, "Touch top/Ending Detail deleting destructor", 0x1D2690, "558BEC6AFF68C75B760064A100000000505657A1D0A07F0033C5508D45F464A3", typeof(TouchTopMenuDeletingDestructorDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.ClassicTopMenuActionDispatcher, "Classic top-menu action dispatcher", 0x2A8630, "558BEC83E4F851568BF18B0683F8070F87E1000000FF24852C876A008B4E04E84CFEFFFF8B4E04E8", typeof(TopMenuActionDispatcherDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.TouchTopMenuActionDispatcher, "Touch/mouse top-menu action dispatcher", 0x2BD1F0, "558BEC83E4F851568BF18B0683F8070F87E1000000FF2485ECD26B008B4E04E80CFEFFFF8B4E04E8", typeof(TopMenuActionDispatcherDelegate), X86CallingConvention.MicrosoftThiscall },
    };

    public static TheoryData<HookId, string, uint, uint, uint, string> ExpectedTopMenuCallSites => new()
    {
        { HookId.ClassicTopMenuTimeLabelCallSite, "Classic top-menu time label call site", 0x1D0AA5, 0x1D0AAA, 0x2400B0, "E806F60600" },
        { HookId.ClassicTopMenuCurrencyLabelCallSite, "Classic top-menu currency label call site", 0x1D0B4F, 0x1D0B54, 0x2400B0, "E85CF50600" },
        { HookId.ClassicTopMenuSingleFooterLabelCallSite, "Classic top-menu single footer label call site", 0x1D0D73, 0x1D0D78, 0x2400B0, "E838F30600" },
        { HookId.ClassicTopMenuFirstFooterLabelCallSite, "Classic top-menu first footer label call site", 0x1D0DB7, 0x1D0DBC, 0x2400B0, "E8F4F20600" },
        { HookId.ClassicTopMenuSecondFooterLabelCallSite, "Classic top-menu second footer label call site", 0x1D0E00, 0x1D0E05, 0x2400B0, "E8ABF20600" },
        { HookId.ClassicTopMenuContextLabelCallSite, "Classic top-menu context label call site", 0x1D0ECE, 0x1D0ED3, 0x2400B0, "E8DDF10600" },
        { HookId.StatusBarHiddenLabelCallSite, "StatusBar hidden initial label call site", 0x22EFA1, 0x22EFA6, 0x2400B0, "E80A110100" },
        { HookId.StatusBarEmptyLineLabelCallSite, "StatusBar empty line label call site", 0x22F2D7, 0x22F2DC, 0x2400B0, "E8D40D0100" },
        { HookId.TouchStatusBarInitialLabelCallSite, "Touch StatusBar initial label call site", 0x22ECE6, 0x22ECEB, 0x2400B0, "E8C5130100" },
        { HookId.ClassicTopMenuCaptionLabelCallSite, "Classic top-menu caption label call site", 0x1D1799, 0x1D179E, 0x2400B0, "E812E90600" },
        { HookId.ClassicTopMenuMemberNameLabelCallSite, "Classic top-menu member-name label call site", 0x23B1CB, 0x23B1D0, 0x2400B0, "E8E04E0000" },
        { HookId.ClassicStatusRowLabelCallSite, "Classic status row-label call site", 0x23A2ED, 0x23A2F2, 0x2400B0, "E8BE5D0000" },
        { HookId.ClassicStatusRowZeroValueCallSite, "Classic status row-zero value call site", 0x23A54D, 0x23A552, 0x2400B0, "E85E5B0000" },
        { HookId.ClassicStatusUnavailableValueCallSite, "Classic status unavailable-value call site", 0x23A3BD, 0x23A3C2, 0x2400B0, "E8EE5C0000" },
        { HookId.ClassicStatusCurrentValueCallSite, "Classic status current-value call site", 0x23A5EA, 0x23A5EF, 0x2400B0, "E8C15A0000" },
        { HookId.ClassicStatusMaximumValueCallSite, "Classic status maximum-value call site", 0x23A692, 0x23A697, 0x2400B0, "E8195A0000" },
        { HookId.ClassicStatusExtraLabelCallSite, "Classic status extra-label call site", 0x23A736, 0x23A73B, 0x2400B0, "E875590000" },
        { HookId.TouchTopMenuTimeLabelCallSite, "Touch top-menu time label call site", 0x221A4A, 0x221A4F, 0x2400B0, "E861E60100" },
        { HookId.TouchTopMenuCurrencyLabelCallSite, "Touch top-menu currency label call site", 0x221B17, 0x221B1C, 0x2400B0, "E894E50100" },
        { HookId.TouchTopMenuCaptionLabelCallSite, "Touch top-menu caption label call site", 0x2221C1, 0x2221C6, 0x2400B0, "E8EADE0100" },
        { HookId.TouchTopMenuMemberNameLabelCallSite, "Touch top-menu member-name label call site", 0x23A9E1, 0x23A9E6, 0x2400B0, "E8CA560000" },
        { HookId.TouchTopMenuReserveNameLabelCallSite, "Touch top-menu reserve-name label call site", 0x23AE35, 0x23AE3A, 0x2400B0, "E876520000" },
        { HookId.CompactStatusRowLabelCallSite, "Compact status row-label call site", 0x239736, 0x23973B, 0x2400B0, "E875690000" },
        { HookId.CompactStatusRowZeroValueCallSite, "Compact status row-zero value call site", 0x23988C, 0x239891, 0x2400B0, "E81F680000" },
        { HookId.CompactStatusCurrentValueCallSite, "Compact status current-value call site", 0x23990F, 0x239914, 0x2400B0, "E89C670000" },
        { HookId.CompactStatusMaximumValueCallSite, "Compact status maximum-value call site", 0x2399A5, 0x2399AA, 0x2400B0, "E806670000" },
        { HookId.CompactStatusExtraLabelCallSite, "Compact status extra-label call site", 0x239A6E, 0x239A73, 0x2400B0, "E83D660000" },
        { HookId.StatusBarGlyphRendererCallSite, "StatusBar glyph-renderer call site", 0x22F3B8, 0x22F3BD, 0x22E080, "E8C3ECFFFF" },
    };

    public static TheoryData<HookId, string, uint, uint, uint, string> ExpectedSettingsCallSites => new()
    {
        { HookId.SteamSettingsCategoryLabelCallSite, "Steam Settings category label call site", 0x1EFD44, 0x1EFD49, 0x2400B0, "E867030500" },
        { HookId.SteamSettingsRowLabelCallSite, "Steam Settings row label call site", 0x1F091D, 0x1F0922, 0x2400B0, "E88EF70400" },
        { HookId.SteamSettingsTitleResolutionValueLabelCallSite, "Steam Settings title-resolution value label call site", 0x1F0AB4, 0x1F0AB9, 0x2400B0, "E8F7F50400" },
        { HookId.SteamSettingsSelectedValueLabelCallSite, "Steam Settings selected-value label call site", 0x1F0D05, 0x1F0D0A, 0x2400B0, "E8A6F30400" },
        { HookId.SteamSettingsResolutionHeadingLabelCallSite, "Steam Settings resolution heading label call site", 0x1FA41A, 0x1FA41F, 0x2400B0, "E8915C0400" },
        { HookId.SteamSettingsResolutionEntryLabelCallSite, "Steam Settings resolution entry label call site", 0x1FA9C0, 0x1FA9C5, 0x2400B0, "E8EB560400" },
        { HookId.SteamSettingsConfirmationAChoiceLabelCallSite, "Steam Settings confirmation A choice label call site", 0x1F42EC, 0x1F42F1, 0x2400B0, "E8BFBD0400" },
        { HookId.SteamSettingsConfirmationBChoiceLabelCallSite, "Steam Settings confirmation B choice label call site", 0x1F4BF9, 0x1F4BFE, 0x2400B0, "E8B2B40400" },
        { HookId.TouchSettingsFirstPermanentControlLabelCallSite, "Touch Settings first permanent-control label call site", 0x1E0499, 0x1E049E, 0x2400B0, "E812FC0500" },
        { HookId.TouchSettingsSecondPermanentControlLabelCallSite, "Touch Settings second permanent-control label call site", 0x1E0786, 0x1E078B, 0x2400B0, "E825F90500" },
        { HookId.TouchSettingsHeadingLabelCallSite, "Touch Settings heading label call site", 0x1E0810, 0x1E0815, 0x2400B0, "E89BF80500" },
        { HookId.TouchSettingsConfirmationAChoiceLabelCallSite, "Touch Settings confirmation A choice label call site", 0x1E4656, 0x1E465B, 0x2400B0, "E855BA0500" },
        { HookId.TouchSettingsConfirmationBChoiceLabelCallSite, "Touch Settings confirmation B choice label call site", 0x1E5179, 0x1E517E, 0x2400B0, "E832AF0500" },
    };

    [Theory]
    [MemberData(nameof(ExpectedContracts))]
    public void HookContract_PreservesStableIdentityBytesAndAbi(
        HookId id,
        string symbol,
        uint rva,
        string expectedHex,
        Type delegateType,
        X86CallingConvention callingConvention)
    {
        var contract = Assert.Single(GameVersionCatalog.Hooks, candidate => candidate.Id == id);

        Assert.Equal(symbol, contract.Symbol);
        Assert.Equal(rva, contract.Rva);
        Assert.Equal(expectedHex, Convert.ToHexString(contract.ExpectedBytes.AsSpan()));
        Assert.Equal(NativeHookKind.FunctionEntry, contract.Kind);
        Assert.Equal(delegateType, contract.DelegateType);
        Assert.Equal(callingConvention, contract.CallingConvention);
        Assert.True(typeof(Delegate).IsAssignableFrom(contract.DelegateType!));
    }

    [Fact]
    public void HookCatalog_HasExactlyOneHundredSeventySixUniqueContracts()
    {
        Assert.Equal(176, GameVersionCatalog.Hooks.Count);
        Assert.Equal(176, GameVersionCatalog.Hooks.Select(contract => contract.Id).Distinct().Count());
        Assert.Equal(176, GameVersionCatalog.Hooks.Select(contract => contract.Symbol).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(176, GameVersionCatalog.Hooks.Select(contract => contract.Rva).Distinct().Count());
    }

    [Fact]
    public void IntroDispatcherUsesTheAuditedThiscallEntry()
    {
        var contract = GameVersionCatalog.Get(HookId.FieldOpcodeDispatcher);
        Assert.Equal(0x1619E0u, contract.Rva);
        Assert.Equal("558BEC8B450853568BF1573DFF000000", Convert.ToHexString(contract.ExpectedBytes.AsSpan()));
        Assert.Equal(typeof(FieldOpcodeDispatcherDelegate), contract.DelegateType);
        Assert.Equal(X86CallingConvention.MicrosoftThiscall, contract.CallingConvention);
    }

    [Fact]
    public void EveryCatalogDelegateCarriesOfficialReloadedX86FunctionMetadata()
    {
        foreach (var contract in GameVersionCatalog.Hooks.Where(contract => contract.Kind == NativeHookKind.FunctionEntry))
        {
            var functionAttribute = Assert.Single(
                contract.DelegateType!.GetCustomAttributesData(),
                attribute => attribute.AttributeType.FullName ==
                    "Reloaded.Hooks.Definitions.X86.FunctionAttribute");

            var conventionValue = Convert.ToInt32(functionAttribute.ConstructorArguments.Single().Value);
            var expectedConvention = contract.CallingConvention == X86CallingConvention.MicrosoftFastcall
                ? CallingConventions.Fastcall
                : CallingConventions.MicrosoftThiscall;
            Assert.Equal((int)expectedConvention, conventionValue);
        }
    }

    [Fact]
    public void ChoiceConfirmCallSite_IsAnExactNonCallableAssemblyContract()
    {
        var contract = GameVersionCatalog.Get(HookId.MsgWindowChoiceConfirmCallSite);

        Assert.Equal("MsgWindow choice-confirm close call site", contract.Symbol);
        Assert.Equal(GameVersionCatalog.MsgWindowChoiceConfirmCallRva, contract.Rva);
        Assert.Equal("E8E1E5FFFF", Convert.ToHexString(contract.ExpectedBytes.AsSpan()));
        Assert.Equal(NativeHookKind.AssemblyCallSite, contract.Kind);
        Assert.Null(contract.DelegateType);
        Assert.Null(contract.CallingConvention);
        Assert.Equal(GameVersionCatalog.MsgWindowChoiceConfirmReturnRva, contract.Rva + contract.ExpectedBytes.Length);
    }

    [Theory]
    [MemberData(nameof(ExpectedTopMenuCallSites))]
    public void TopMenuCallSite_IsAnExactFiveByteDirectCallToItsAuditedTarget(
        HookId id,
        string symbol,
        uint probeRva,
        uint returnRva,
        uint targetRva,
        string expectedHex)
    {
        var contract = GameVersionCatalog.Get(id);

        Assert.Equal(symbol, contract.Symbol);
        Assert.Equal(probeRva, contract.Rva);
        Assert.Equal(expectedHex, Convert.ToHexString(contract.ExpectedBytes.AsSpan()));
        Assert.Equal(NativeHookKind.AssemblyCallSite, contract.Kind);
        Assert.Null(contract.DelegateType);
        Assert.Null(contract.CallingConvention);
        Assert.Equal(5, contract.ExpectedBytes.Length);
        Assert.Equal(0xE8, contract.ExpectedBytes[0]);
        Assert.Equal(returnRva, contract.Rva + contract.ExpectedBytes.Length);

        var displacement = BinaryPrimitives.ReadInt32LittleEndian(contract.ExpectedBytes.AsSpan()[1..]);
        Assert.Equal(targetRva, checked((uint)(returnRva + displacement)));
    }

    [Theory]
    [MemberData(nameof(ExpectedSettingsCallSites))]
    public void SettingsCallSite_IsAnExactFiveByteDirectCallToMenuTextLabelFactory(
        HookId id,
        string symbol,
        uint probeRva,
        uint returnRva,
        uint targetRva,
        string expectedHex)
    {
        var contract = GameVersionCatalog.Get(id);

        Assert.Equal(symbol, contract.Symbol);
        Assert.Equal(probeRva, contract.Rva);
        Assert.Equal(expectedHex, Convert.ToHexString(contract.ExpectedBytes.AsSpan()));
        Assert.Equal(NativeHookKind.AssemblyCallSite, contract.Kind);
        Assert.Null(contract.DelegateType);
        Assert.Null(contract.CallingConvention);
        Assert.Equal(5, contract.ExpectedBytes.Length);
        Assert.Equal(0xE8, contract.ExpectedBytes[0]);
        Assert.Equal(returnRva, contract.Rva + contract.ExpectedBytes.Length);

        var displacement = BinaryPrimitives.ReadInt32LittleEndian(contract.ExpectedBytes.AsSpan()[1..]);
        Assert.Equal(targetRva, checked((uint)(returnRva + displacement)));
    }

    [Fact]
    public void HookContract_RejectsContradictoryFunctionAndCallSiteAbiMetadata()
    {
        Assert.Throws<ArgumentException>(() => new HookContract(
            HookId.MsgWindowChoiceConfirmCallSite,
            "function without ABI",
            1,
            [0x90],
            NativeHookKind.FunctionEntry,
            delegateType: null,
            callingConvention: null));
        Assert.Throws<ArgumentException>(() => new HookContract(
            HookId.MsgWindowChoiceConfirmCallSite,
            "callsite with delegate",
            1,
            [0x90],
            NativeHookKind.AssemblyCallSite,
            typeof(MsgWindowCloseDelegate),
            callingConvention: null));
        Assert.Throws<ArgumentException>(() => new HookContract(
            HookId.MsgWindowChoiceConfirmCallSite,
            "callsite with convention",
            1,
            [0x90],
            NativeHookKind.AssemblyCallSite,
            delegateType: null,
            callingConvention: X86CallingConvention.MicrosoftThiscall));
    }

    [Fact]
    public void CallbackDelegates_PreserveReviewedPointerAndValueParameters()
    {
        AssertSignature<TitleMenuCallbackDelegate>(typeof(nint), typeof(nint), typeof(nint));
        AssertSignature<ModeSelectCallbackDelegate>(typeof(nint), typeof(int), typeof(int));
        AssertSignature<ControlNextCallbackDelegate>(typeof(nint), typeof(nint), typeof(nint));
        AssertSignature<NameActionCallbackDelegate>(typeof(nint), typeof(int), typeof(int));
        AssertSignature<NameDirectEntryActivationDelegate>(typeof(nint));
        AssertSignature<NameDirectEntryCloseDelegate>(typeof(nint));
        AssertSignature<NameGridRefreshDelegate>(typeof(nint));
        AssertSignature<NsMenuControlBinderDelegate>(typeof(nint), typeof(nint), typeof(int));
    }

    [Fact]
    public void StartupAndTitleDelegatesPreserveAuditedParameterAndReturnContracts()
    {
        AssertSignatureWithReturn<TextManagerGetMsgDelegate>(typeof(nint), typeof(nint), typeof(nint), typeof(int), typeof(int));
        AssertSignatureWithReturn<SceneManagerNextSceneDelegate>(typeof(void), typeof(uint));
        AssertSignatureWithReturn<TitleRowFactoryDelegate>(typeof(nint), typeof(nint));
        AssertSignatureWithReturn<OpeTextResolverDelegate>(
            typeof(nint), typeof(nint), typeof(nint), typeof(int), typeof(int));
        AssertSignatureWithReturn<NsMenuCustomButtonConstructorDelegate>(typeof(nint), typeof(nint));
        AssertSignature<NameConfirmationBuilderDelegate>(
            typeof(nint), typeof(uint), typeof(uint), typeof(uint), typeof(uint), typeof(uint), typeof(uint));
    }

    [Fact]
    public void EntryDelegates_PreserveReviewedNativeReturnTypes()
    {
        AssertReturnType<ModeSelectSteamInitDelegate>(typeof(byte));
        AssertReturnType<OpeManualSceneInitDelegate>(typeof(byte));
        AssertReturnType<NameInputSceneInitDelegate>(typeof(byte));
        AssertReturnType<TitleMenuModeEnterDelegate>(typeof(void));
    }

    [Fact]
    public void ExtrasSettingsDialogueAndMenuDelegatesPreserveAuditedNativeContracts()
    {
        AssertSignatureWithReturn<GallerySceneSwitchNodeDelegate>(typeof(nint), typeof(nint), typeof(int), typeof(uint));
        AssertSignature<ExtrasHubOnEnterDelegate>(typeof(nint));
        AssertSignature<EndingLogOnEnterDelegate>(typeof(nint));
        AssertSignature<EndingDetailOnEnterDelegate>(typeof(nint));
        AssertSignature<ExtrasNodeOnExitDelegate>(typeof(nint));
        AssertSignatureWithReturn<ExtrasHubDeletingDestructorDelegate>(typeof(nint), typeof(nint), typeof(uint));
        AssertSignatureWithReturn<EndingLogDeletingDestructorDelegate>(typeof(nint), typeof(nint), typeof(uint));
        AssertSignature<ExtrasHubCallbackDelegate>(typeof(nint), typeof(int), typeof(int));
        AssertSignature<EndingLogCallbackDelegate>(typeof(nint), typeof(int), typeof(int));
        AssertSignature<EndingDetailCallbackDelegate>(typeof(nint), typeof(int), typeof(int));
        AssertSignature<ExtrasLogTransitionDelegate>(typeof(nint));
        AssertSignature<ExtrasDetailTransitionDelegate>(typeof(nint));
        AssertSignature<SaveLoadConfirmationBuilderDelegate>(typeof(nint), typeof(int));
        AssertSignature<SaveLoadNodeDestructorDelegate>(typeof(nint));
        AssertSignatureWithReturn<MenuNodeConfigSteamConstructorDelegate>(typeof(nint), typeof(nint), typeof(int));
        AssertSignature<MenuNodeConfigSteamBuilderDelegate>(typeof(nint));
        AssertSignature<MenuNodeConfigSteamDestructorDelegate>(typeof(nint));
        AssertSignature<MenuNodeConfigSteamPageBuilderDelegate>(typeof(nint), typeof(uint));
        AssertSignature<SteamSettingsCallbackDelegate>(typeof(nint), typeof(int), typeof(int));
        AssertSignature<SteamSettingsSetterInvokerDelegate>(typeof(nint), typeof(int));
        AssertSignature<SteamSettingsNestedBuilderDelegate>(typeof(nint));
        AssertSignatureWithReturn<MenuNodeConfigConstructorDelegate>(typeof(nint), typeof(nint));
        AssertSignature<MenuNodeConfigBuilderDelegate>(typeof(nint));
        AssertSignature<MenuNodeConfigDestructorDelegate>(typeof(nint));
        AssertSignatureWithReturn<TouchSettingsPageTransitionDelegate>(typeof(nint), typeof(nint), typeof(int));
        AssertSignature<TouchSettingsCallbackDelegate>(typeof(nint), typeof(int), typeof(int));
        AssertSignatureWithReturn<TouchSettingsValueMutationDelegate>(typeof(byte), typeof(nint), typeof(int), typeof(int), typeof(int));
        AssertSignature<TouchSettingsNestedBuilderDelegate>(typeof(nint));
        AssertSignature<MsgWindowOpenDelegate>(typeof(nint), typeof(uint));
        AssertSignature<MsgWindowUpdateDelegate>(typeof(nint), typeof(uint));
        AssertSignature<MsgWindowCloseDelegate>(typeof(nint), typeof(uint));
        AssertSignature<MsgWindowChoiceConfirmProbeDelegate>(typeof(nint));
        AssertSignature<ClassicTopMenuBuilderDelegate>(typeof(nint), typeof(uint));
        AssertSignature<TouchTopMenuBuilderDelegate>(typeof(nint), typeof(uint));
        AssertSignatureWithReturn<MenuTextLabelFactoryDelegate>(typeof(nint), typeof(nint), typeof(nint), typeof(nint), typeof(int));
        AssertSignature<StatusBarFormatScopeDelegate>(typeof(nint), typeof(nint), typeof(uint));
        AssertSignatureWithReturn<StatusBarGlyphRendererDelegate>(typeof(nint), typeof(nint), typeof(nint), typeof(nint));
        AssertSignature<StatusBarDestructorDelegate>(typeof(nint));
        AssertSignatureWithReturn<ClassicTopMenuDeletingDestructorDelegate>(typeof(nint), typeof(nint), typeof(uint));
        AssertSignatureWithReturn<TouchTopMenuDeletingDestructorDelegate>(typeof(nint), typeof(nint), typeof(uint));
        AssertSignature<TopMenuActionDispatcherDelegate>(typeof(nint));
        AssertSignature<NativeCallSiteProbeDelegate>();
    }

    [Theory]
    [InlineData(HookId.WorldNavigationEpochTickCallSite, "World navigation Epoch task tick", 0x2766EA, 0x28E1D0, "E8E17A0100")]
    [InlineData(HookId.WorldNavigationDactylTickCallSite, "World navigation Dactyl task tick", 0x276718, 0x28A1E0, "E8C33A0100")]
    public void VehicleTickCallSite_IsAnExactFiveByteDirectCallToItsVehicleTask(
        HookId id, string symbol, uint probeRva, uint targetRva, string expectedHex)
    {
        var contract = GameVersionCatalog.Get(id);
        Assert.Equal(symbol, contract.Symbol);
        Assert.Equal(probeRva, contract.Rva);
        Assert.Equal(expectedHex, Convert.ToHexString(contract.ExpectedBytes.AsSpan()));
        Assert.Equal(NativeHookKind.AssemblyCallSite, contract.Kind);
        Assert.Null(contract.DelegateType);
        Assert.Null(contract.CallingConvention);
        var displacement = BinaryPrimitives.ReadInt32LittleEndian(contract.ExpectedBytes.AsSpan()[1..]);
        Assert.Equal(targetRva, checked((uint)(probeRva + 5 + displacement)));
    }

    [Theory]
    [InlineData(HookId.WorldNavigationEpochPadGateInstruction, 0x28E8C2)]
    [InlineData(HookId.WorldNavigationEpochPadInstruction, 0x28EAB9)]
    [InlineData(HookId.WorldNavigationDactylPadGateInstruction, 0x28A761)]
    [InlineData(HookId.WorldNavigationDactylPadInstruction, 0x28A94C)]
    public void VehiclePadSite_IsTheSameSixByteCombineInstructionAsWalking(HookId id, uint rva)
    {
        var contract = GameVersionCatalog.Get(id);
        Assert.Equal(rva, contract.Rva);
        Assert.Equal("0BBE1C330000", Convert.ToHexString(contract.ExpectedBytes.AsSpan()));
        Assert.Equal(NativeHookKind.AssemblyInstructionSite, contract.Kind);
        Assert.Null(contract.DelegateType);
        Assert.Null(contract.CallingConvention);
    }

    [Fact]
    public void TimeGaugeDelegatesPreserveTheAuditedThiscallContracts()
    {
        AssertSignatureWithReturn<TimeGaugeSceneInitDelegate>(typeof(byte), typeof(nint));
        AssertSignature<TimeGaugeSceneUpdateDelegate>(typeof(nint), typeof(float));
    }

    [Fact]
    public void MisleadingUnqualifiedSettingsMutationNamesAreRemoved()
    {
        Assert.False(Enum.TryParse<HookId>("SettingsValueMutation", out _));
        Assert.Null(typeof(HookContract).Assembly.GetType(
            "ChronoTriggerAccessibility.Native.Hooks.SettingsValueMutationDelegate",
            throwOnError: false,
            ignoreCase: false));
    }

    [Fact]
    public void MsgWindowChoiceConfirmReturnFollowsItsExactCallInstruction()
    {
        Assert.Equal(GameVersionCatalog.MsgWindowChoiceConfirmCallRva + 5, GameVersionCatalog.MsgWindowChoiceConfirmReturnRva);
    }

    [Fact]
    public void ChoiceConfirmProbeCallbackUsesOfficialReloadedCdeclMetadata()
    {
        var functionAttribute = Assert.Single(
            typeof(MsgWindowChoiceConfirmProbeDelegate).GetCustomAttributesData(),
            attribute => attribute.AttributeType.FullName ==
                "Reloaded.Hooks.Definitions.X86.FunctionAttribute");
        Assert.Equal(
            (int)CallingConventions.Cdecl,
            Convert.ToInt32(functionAttribute.ConstructorArguments.Single().Value));
    }

    [Fact]
    public void NativeCallSiteProbeCallbackUsesOfficialReloadedCdeclMetadata()
    {
        var functionAttribute = Assert.Single(
            typeof(NativeCallSiteProbeDelegate).GetCustomAttributesData(),
            attribute => attribute.AttributeType.FullName ==
                "Reloaded.Hooks.Definitions.X86.FunctionAttribute");
        Assert.Equal(
            (int)CallingConventions.Cdecl,
            Convert.ToInt32(functionAttribute.ConstructorArguments.Single().Value));
    }

    [Fact]
    public void ModeValueGetterWrapperDelegateUsesOfficialReloadedThiscallMetadata()
    {
        AssertSignatureWithReturn<ModeValueGetterDelegate>(typeof(int), typeof(nint));
        var functionAttribute = Assert.Single(
            typeof(ModeValueGetterDelegate).GetCustomAttributesData(),
            attribute => attribute.AttributeType.FullName ==
                "Reloaded.Hooks.Definitions.X86.FunctionAttribute");
        Assert.Equal(
            (int)CallingConventions.MicrosoftThiscall,
            Convert.ToInt32(functionAttribute.ConstructorArguments.Single().Value));
    }

    private static void AssertSignature<TDelegate>(params Type[] parameterTypes)
        where TDelegate : Delegate
    {
        var invoke = typeof(TDelegate).GetMethod("Invoke")
            ?? throw new InvalidOperationException($"{typeof(TDelegate).Name} has no Invoke method.");

        Assert.Equal(typeof(void), invoke.ReturnType);
        Assert.Equal(parameterTypes, invoke.GetParameters().Select(parameter => parameter.ParameterType));
    }

    private static void AssertReturnType<TDelegate>(Type returnType)
        where TDelegate : Delegate
    {
        var invoke = typeof(TDelegate).GetMethod("Invoke")
            ?? throw new InvalidOperationException($"{typeof(TDelegate).Name} has no Invoke method.");

        Assert.Equal(returnType, invoke.ReturnType);
    }

    private static void AssertSignatureWithReturn<TDelegate>(Type returnType, params Type[] parameterTypes)
        where TDelegate : Delegate
    {
        var invoke = typeof(TDelegate).GetMethod("Invoke")
            ?? throw new InvalidOperationException($"{typeof(TDelegate).Name} has no Invoke method.");

        Assert.Equal(returnType, invoke.ReturnType);
        Assert.Equal(parameterTypes, invoke.GetParameters().Select(parameter => parameter.ParameterType));
    }
}

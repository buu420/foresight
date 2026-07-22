using ChronoTriggerAccessibility.Native.Hooks;
using Reloaded.Hooks.Definitions.X86;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Hooks;

public sealed class GameVersionCatalogTests
{
    public static TheoryData<HookId, string, uint, string, Type, X86CallingConvention> ExpectedContracts => new()
    {
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
        { HookId.GallerySceneSwitchNode, "GalleryScene::switchNode", 0x2A52B0, "558BEC6AFF6838AB770064A1000000005083EC64A1D0A07F", typeof(GallerySceneSwitchNodeDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.ExtrasHubOnEnter, "Extras Hub onEnter", 0x1DB570, "568BF1FF15AC5A7800FFB6CC0200008BCEE80A0000005EC3", typeof(ExtrasHubOnEnterDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.EndingLogOnEnter, "Ending Log onEnter", 0x1D3980, "568BF1FF15AC5A7800FFB6C80200008BCEE80A0000005EC3", typeof(EndingLogOnEnterDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.EndingDetailOnEnter, "Ending Detail onEnter", 0x1D2730, "568BF1FF15AC5A7800518BCEE82F0000005EC3", typeof(EndingDetailOnEnterDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.ExtrasHubCallback, "Extras hub callback", 0x1DC610, "558BEC83E4F88B450883EC08568BF15783E8000F849E0000", typeof(ExtrasHubCallbackDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.EndingLogCallback, "Ending Log callback", 0x1D4850, "558BEC8B4508568BF15783E8000F849200000083E8017435", typeof(EndingLogCallbackDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.EndingDetailCallback, "Ending Detail callback", 0x1D35A0, "558BEC8B4508568BF183E800745983E801744083E8010F85", typeof(EndingDetailCallbackDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.MenuNodeConfigSteamConstructor, "MenuNodeConfigSteam constructor", 0x1ECB00, "558BEC6AFF681044760064A1000000005056A1D0A07F0033C5508D45F464A300", typeof(MenuNodeConfigSteamConstructorDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.MenuNodeConfigSteamBuilder, "MenuNodeConfigSteam builder", 0x1ED020, "558BEC6AFF684102770064A1000000005081EC640E0000A1D0A07F0033C58945", typeof(MenuNodeConfigSteamBuilderDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.MenuNodeConfigSteamDestructor, "MenuNodeConfigSteam destructor", 0x1EC970, "558BEC6AFF689EC6760064A100000000505657A1D0A07F0033C5508D45F464A3", typeof(MenuNodeConfigSteamDestructorDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.SettingsValueMutation, "Settings value mutation", 0x1E3980, "558BEC538BD95669750C98000000", typeof(SettingsValueMutationDelegate), X86CallingConvention.MicrosoftThiscall },
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
    public void HookCatalog_HasExactlyFortyFourUniqueContracts()
    {
        Assert.Equal(44, GameVersionCatalog.Hooks.Count);
        Assert.Equal(44, GameVersionCatalog.Hooks.Select(contract => contract.Id).Distinct().Count());
        Assert.Equal(44, GameVersionCatalog.Hooks.Select(contract => contract.Symbol).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(44, GameVersionCatalog.Hooks.Select(contract => contract.Rva).Distinct().Count());
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
        AssertSignature<ExtrasHubCallbackDelegate>(typeof(nint), typeof(int), typeof(int));
        AssertSignature<EndingLogCallbackDelegate>(typeof(nint), typeof(int), typeof(int));
        AssertSignature<EndingDetailCallbackDelegate>(typeof(nint), typeof(int), typeof(int));
        AssertSignatureWithReturn<MenuNodeConfigSteamConstructorDelegate>(typeof(nint), typeof(nint), typeof(int));
        AssertSignature<MenuNodeConfigSteamBuilderDelegate>(typeof(nint));
        AssertSignature<MenuNodeConfigSteamDestructorDelegate>(typeof(nint));
        AssertSignatureWithReturn<SettingsValueMutationDelegate>(typeof(byte), typeof(nint), typeof(int), typeof(int), typeof(int));
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

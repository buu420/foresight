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
        Assert.Equal(delegateType, contract.DelegateType);
        Assert.Equal(callingConvention, contract.CallingConvention);
        Assert.True(typeof(Delegate).IsAssignableFrom(contract.DelegateType));
    }

    [Fact]
    public void HookCatalog_HasExactlyTwentyOneUniqueContracts()
    {
        Assert.Equal(21, GameVersionCatalog.Hooks.Count);
        Assert.Equal(21, GameVersionCatalog.Hooks.Select(contract => contract.Id).Distinct().Count());
        Assert.Equal(21, GameVersionCatalog.Hooks.Select(contract => contract.Symbol).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(21, GameVersionCatalog.Hooks.Select(contract => contract.Rva).Distinct().Count());
    }

    [Fact]
    public void EveryCatalogDelegateCarriesOfficialReloadedX86FunctionMetadata()
    {
        foreach (var contract in GameVersionCatalog.Hooks)
        {
            var functionAttribute = Assert.Single(
                contract.DelegateType.GetCustomAttributesData(),
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

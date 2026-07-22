using ChronoTriggerAccessibility.Native.Hooks;
using Reloaded.Hooks.Definitions.X86;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Hooks;

public sealed class GameVersionCatalogTests
{
    public static TheoryData<HookId, string, uint, string, Type, X86CallingConvention> ExpectedContracts => new()
    {
        { HookId.TextManagerGetMsg, "TextManager::getMsg", 0x1B9110, "558BEC6AFF68A1AD760064A100000000", typeof(TextManagerGetMsgDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.SceneManagerCreate, "SceneManager::create", 0x297860, "558BEC6AFF68F873760064A100000000", typeof(SceneManagerCreateDelegate), X86CallingConvention.MicrosoftFastcall },
        { HookId.SceneManagerNextScene, "SceneManager::NextScene", 0x297B60, "558BEC6AFF68E894770064A100000000", typeof(SceneManagerNextSceneDelegate), X86CallingConvention.MicrosoftFastcall },
        { HookId.ModeSelectSteamInit, "ModeSelectSteam::init", 0x2A9C60, "558BEC6AFF68B2AE770064A100000000", typeof(ModeSelectSteamInitDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.OpeManualSceneInit, "OpeManualScene::init", 0x2ADB50, "558BEC6AFF68E4B5770064A100000000", typeof(OpeManualSceneInitDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.NameInputSceneUpdate, "NameInputScene::update", 0x2C2C50, "558BEC83E4F8515356578BF980BF9402", typeof(NameInputSceneUpdateDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.TitleMenuModeEnter, "TitleMenuMode::enter", 0x2CF560, "558BEC6AFF6884E4770064A100000000", typeof(TitleMenuModeEnterDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.TitleRowFactory, "Title row factory", 0x2CD7A0, "558BEC6AFF6811E2770064A100000000", typeof(TitleRowFactoryDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.TitleSceneUpdate, "TitleScene::update", 0x2D1030, "558BECF30F104508568BF18B8E900200", typeof(TitleSceneUpdateDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.TitleMenuCallback, "TitleMenu callback", 0x2D12A0, "558BEC8B450C56578BF18B388B45088B", typeof(TitleMenuCallbackDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.NsMenuFocusSetter, "nsMenu focus setter", 0x1DD3E0, "558BEC83EC208BC1538B5D08578DB8C40200008945FCC680CC02000001897DEC", typeof(NsMenuFocusSetterDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.ModeSelectCallback, "ModeSelect callback", 0x2AB9E0, "558BEC83E4F883EC148B4508538BD9895C2404565783F8030F87B2020000FF24", typeof(ModeSelectCallbackDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.ControlNextCallback, "Control Next callback", 0x2AE840, "558BEC83E4F8518B4508568BF18B0083E800740583E8027523C705CCC3810018", typeof(ControlNextCallbackDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.NameActionCallback, "Name action callback", 0x2C1760, "558BEC6AFF68FFD4770064A1000000005083EC70A1D0A07F0033C58945F05657", typeof(NameActionCallbackDelegate), X86CallingConvention.MicrosoftThiscall },
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
    public void HookCatalog_HasExactlyFourteenUniqueContracts()
    {
        Assert.Equal(14, GameVersionCatalog.Hooks.Count);
        Assert.Equal(14, GameVersionCatalog.Hooks.Select(contract => contract.Id).Distinct().Count());
        Assert.Equal(14, GameVersionCatalog.Hooks.Select(contract => contract.Symbol).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(14, GameVersionCatalog.Hooks.Select(contract => contract.Rva).Distinct().Count());
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
        AssertSignature<ControlNextCallbackDelegate>(typeof(nint), typeof(nint));
        AssertSignature<NameActionCallbackDelegate>(typeof(nint), typeof(int), typeof(int));
    }

    [Fact]
    public void StartupAndTitleDelegatesPreserveAuditedParameterAndReturnContracts()
    {
        AssertSignatureWithReturn<TextManagerGetMsgDelegate>(typeof(nint), typeof(nint), typeof(nint), typeof(int), typeof(int));
        AssertSignatureWithReturn<SceneManagerNextSceneDelegate>(typeof(void), typeof(uint));
        AssertSignatureWithReturn<TitleRowFactoryDelegate>(typeof(nint), typeof(nint));
    }

    [Fact]
    public void EntryDelegates_PreserveReviewedNativeReturnTypes()
    {
        AssertReturnType<ModeSelectSteamInitDelegate>(typeof(byte));
        AssertReturnType<OpeManualSceneInitDelegate>(typeof(byte));
        AssertReturnType<TitleMenuModeEnterDelegate>(typeof(void));
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

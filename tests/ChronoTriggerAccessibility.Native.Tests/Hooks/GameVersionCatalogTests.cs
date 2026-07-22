using ChronoTriggerAccessibility.Native.Hooks;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Hooks;

public sealed class GameVersionCatalogTests
{
    public static TheoryData<HookId, string, uint, string, Type, X86CallingConvention> ExpectedContracts => new()
    {
        { HookId.TextManagerGetMsg, "TextManager::getMsg", 0x1B92D0, "558BEC518B450CFF7510C745FC00000000", typeof(TextManagerGetMsgDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.SceneManagerCreate, "SceneManager::create", 0x297860, "558BEC6AFF68F873760064A100000000", typeof(SceneManagerCreateDelegate), X86CallingConvention.MicrosoftFastcall },
        { HookId.ModeSelectSteamInit, "ModeSelectSteam::init", 0x2A9C60, "558BEC6AFF68B2AE770064A100000000", typeof(ModeSelectSteamInitDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.OpeManualSceneInit, "OpeManualScene::init", 0x2ADB50, "558BEC6AFF68E4B5770064A100000000", typeof(OpeManualSceneInitDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.NameInputSceneUpdate, "NameInputScene::update", 0x2C2C50, "558BEC83E4F8515356578BF980BF9402", typeof(NameInputSceneUpdateDelegate), X86CallingConvention.MicrosoftThiscall },
        { HookId.TitleMenuModeEnter, "TitleMenuMode::enter", 0x2CF560, "558BEC6AFF6884E4770064A100000000", typeof(TitleMenuModeEnterDelegate), X86CallingConvention.MicrosoftThiscall },
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
    public void HookCatalog_HasExactlyTwelveUniqueContracts()
    {
        Assert.Equal(12, GameVersionCatalog.Hooks.Count);
        Assert.Equal(12, GameVersionCatalog.Hooks.Select(contract => contract.Id).Distinct().Count());
        Assert.Equal(12, GameVersionCatalog.Hooks.Select(contract => contract.Symbol).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(12, GameVersionCatalog.Hooks.Select(contract => contract.Rva).Distinct().Count());
    }
}

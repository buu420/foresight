using ChronoTriggerAccessibility.Native.Hooks;
using Reloaded.Hooks.Definitions.X86;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Hooks;

public sealed class BattleHookContractTests
{
    [Theory]
    [InlineData(HookId.BattleHudRefresh, 0x1BDD0u, typeof(BattleMenuMemberDelegate), 0, "558BEC6AFF68C055760064A100000000")]
    [InlineData(HookId.BattleMenuDestructor, 0x14AF0u, typeof(BattleMenuMemberDelegate), 0, "558BEC6AFF68234B760064A100000000")]
    [InlineData(HookId.BattleMessageDisplay, 0x19940u, typeof(BattleSevenWordDelegate), 7, "558BEC6AFF684155760064A100000000")]
    [InlineData(HookId.BattleDamageNumber, 0x1F730u, typeof(BattleSevenWordDelegate), 7, "558BEC6AFF680759760064A100000000")]
    [InlineData(HookId.BattleMiss, 0x1F640u, typeof(BattleMissDelegate), 4, "558BEC6AFF68D758760064A100000000")]
    [InlineData(HookId.BattleDamageRender, 0x1B850u, typeof(BattleRenderDelegate), 2, "558BEC83EC1C8BD15356578D82940100")]
    public void BindsEveryAuditedBattleEntryWithItsActualStackCleanup(HookId id, uint rva, Type type, int words, string bytes)
    {
        var hook = Assert.Single(GameVersionCatalog.Hooks, x => x.Id == id);
        Assert.Equal(rva, hook.Rva);
        Assert.Equal(type, hook.DelegateType);
        Assert.StartsWith(bytes, Convert.ToHexString(hook.ExpectedBytes.AsSpan()));
        Assert.Equal(words + 1, type.GetMethod("Invoke")!.GetParameters().Length);
        Assert.Equal(X86CallingConvention.MicrosoftThiscall, hook.CallingConvention);
        Assert.NotNull(Attribute.GetCustomAttribute(type, typeof(FunctionAttribute)));
    }
}

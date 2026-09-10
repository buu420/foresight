using ChronoTriggerAccessibility.Native.Hooks;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Build;

public sealed class NavigationHookContractTests
{
    [Fact]
    public void NavigationPadProbeUsesAuditedCallAfterInputPermissionChecks()
    {
        Assert.True(Enum.TryParse<HookId>("FieldNavigationPadCallSite", out var id));
        var contract = GameVersionCatalog.Get(id);
        Assert.Equal(0x175A8Du, contract.Rva);
        Assert.Equal(NativeHookKind.AssemblyCallSite, contract.Kind);
        Assert.Equal(Convert.FromHexString("E8FE010000"), contract.ExpectedBytes.ToArray());
    }
}

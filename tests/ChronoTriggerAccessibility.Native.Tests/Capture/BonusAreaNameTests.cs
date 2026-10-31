using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Tests.Support;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class BonusAreaNameTests
{
    private const nuint Image=0x400000,Canvas=0x200000,Manager=0x100000,Banks=0x110000,
        Table=0x160000,Record=0x170000,MapBank=0x150000,Lines=0x180000;
    [Theory]
    [InlineData(532,"MSG_FIELD_MENU_36,Mount Emerald","Mount Emerald")]
    [InlineData(614,"MSG_FIELD_MENU_36,Volcano","Volcano")]
    [InlineData(653,"MSG_FIELD_MENU_36,Dimensional Vortex","Dimensional Vortex")]
    [InlineData(668,"MSG_FIELD_MENU_36,Sanctuaire oublié","Sanctuaire oublié")]
    [InlineData(559,"MSG_FIELD_MENU_36,Tower of \\the Ancients","Tower of the Ancients")]
    public void BonusScenesUseTheirLoadedNativeMenuName(int scene,string raw,string expected)
    {
        Assert.Equal(expected,new NavigationTextCapture(Memory(scene,raw)).FieldName(Image,scene));
    }
    [Fact]
    public void CustomNamesAreResolvedFromTheCurrentSavedCharacterIdentity()
    {
        var m=Memory(614,"MSG_FIELD_MENU_36,<NAME_CRO>'s home").String(Canvas+0x1908,"Alex");
        var reader=new NavigationTextCapture(m);
        Assert.Equal("Alex's home",reader.FieldName(Image,614));
        m.String(Canvas+0x1908,"Sam");Assert.Equal("Sam's home",reader.FieldName(Image,614));
    }
    [Fact]
    public void TheSupportedVolcanoRowHasANamedKeyRatherThanItsNumericRowId()
    {
        Assert.Equal("Volcano",new NavigationTextCapture(Memory(614,"MSG_FIELD_MENU_45,Volcano",139)).FieldName(Image,614));
    }
    [Theory]
    [InlineData(0,"? ? ?")] [InlineData(1,"Truce Canyon")]
    public void AReusedBonusNameKeepsTheNativeDiscoveryRestriction(int flags,string expected)
    {
        var m=Memory(634,"MSG_FIELD_CHUSEI_02,Truce Canyon",30).Word(Canvas+0x688C,(uint)flags)
            .String(Lines+93*24,"MSG_FIELD_OTHER_02,? ? ?");
        Assert.Equal(expected,new NavigationTextCapture(m).FieldName(Image,634));
    }
    [Fact]
    public void ADiscoveryFlagChangingDuringCaptureWithdrawsTheName()
    {
        var m=Memory(634,"MSG_FIELD_CHUSEI_02,Truce Canyon",30).Word(Canvas+0x688C,1);var reads=0;
        m.BeforeRead=a=>{if(a==Canvas+0x688C && ++reads==2)m.Word(a,0);};
        Assert.Null(new NavigationTextCapture(m).FieldName(Image,634));
    }
    [Theory]
    [InlineData("no-name")] [InlineData("zero-name")] [InlineData("missing-row")]
    [InlineData("wrong-scene")] [InlineData("short-table")] [InlineData("bad-count")]
    [InlineData("wrong-key")] [InlineData("unknown-token")]
    public void MissingOrUnprovenNativeNamesDoNotInventLabels(string kind)
    {
        var m=Memory(614,kind=="wrong-key"?"MSG_ITEM_130,Secret":kind=="unknown-token"?"MSG_FIELD_MENU_36,<UNKNOWN>":"MSG_FIELD_MENU_36,Volcano");
        if(kind=="no-name")m.Word(Record+4,255);
        if(kind=="zero-name")m.Word(Record+4,0);
        if(kind=="missing-row")m.Word(Record+4,201);
        if(kind=="wrong-scene")m.Word(Canvas+0x120C0,613);
        if(kind=="short-table")m.Word(Canvas+0xFE2C,614);
        if(kind=="bad-count")m.Word(Canvas+0xFE2C,uint.MaxValue);
        Assert.Null(new NavigationTextCapture(m).FieldName(Image,614));
    }
    [Fact]
    public void ChangingSceneOrTextOwnershipDuringTheReadRejectsTheLabel()
    {
        var m=Memory(614,"MSG_FIELD_MENU_36,Volcano");var reads=0;
        m.BeforeRead=a=>{if(a==Canvas+0x120C0 && ++reads==2)m.Word(a,613);};
        Assert.Null(new NavigationTextCapture(m).FieldName(Image,614));
    }
    private static NavigationMemory Memory(int scene,string text,int row=130)=>new NavigationMemory()
        .Word(Image+0x41B4C4,(uint)Canvas).Word(Image+0x41C3D8,(uint)Manager)
        .Word(Canvas+0x120C0,(uint)scene).Word(Canvas+0xFE28,(uint)Table).Word(Canvas+0xFE2C,669)
        .Word(Table+(nuint)(scene*4),(uint)Record).Word(Record+4,(uint)row)
        .Word(Manager,(uint)Banks).Word(Manager+4,(uint)Banks+71*4)
        .Word(Banks+0x22*4,(uint)MapBank).Word(MapBank,(uint)Lines).Word(MapBank+4,(uint)Lines+200*24)
        .String(Lines+(nuint)((row-1)*24),text);
}

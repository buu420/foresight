using System.Buffers.Binary;
using System.Text;
using ChronoTriggerAccessibility.Core.Battle;
using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Mod.Battle;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Battle;

public sealed class BattleSpeechOrderingTests
{
    private const uint Image=0x400000, Canvas=0x100000, Menu=0x200000;

    [Fact]
    public void ANamePopupBeforeTheNextHudRefreshIsNotLostWhenConfirmEndsTargeting()
    {
        var memory=new Memory(); var events=new List<AccessibilityEvent>();
        memory.Word(Image+0x41B4C4,Canvas);memory.Word(Menu,Image+0x39D884);
        memory.Word(Canvas+0x19FA0,1);memory.String(Canvas+0x1908,"Crono");
        memory.Word(Image+0x41C3D8,0x300000);memory.Word(0x300000,0x300100);memory.Word(0x300004,0x300200);
        memory.Word(0x300100+0x32*4,0x303000);memory.Word(0x303000,0x304000);memory.Word(0x303004,0x304030);
        memory.String(0x304018,"Blue Imp");
        for(uint i=3;i<11;i++)memory.Word(Canvas+0x1A018+i*4,uint.MaxValue);
        memory.Word(Canvas+0x1A018+3*4,1);
        var runtime=new BattleRuntime(events.Add,new BattleKeyboard(_=>false,()=>true),()=>true,_=>{});
        var session=new BattleSession(memory,runtime,_=>{});session.BindImageBase(Image);
        session.Tick(Menu);Assert.True(runtime.IsActive);events.Clear();
        // Native input may choose and confirm a target between HUD refreshes.
        memory.Word(Canvas+0x19EEC,1);memory.Bytes[Menu+0x538]=1;
        for(uint i=0;i<11;i++){
            memory.Word(Canvas+0x1ACD4+i*4,i==0?3u:255u);
            memory.Word(Canvas+0x1A140+i*4,i==0?3u:255u);
        }
        const uint label=0x310000,protocol=0x311000,getter=0x312000;
        memory.Word(Menu+0x234,label);memory.Word(label+0x278,protocol);memory.Word(protocol+8,getter);
        memory.Bytes[getter]=0x8D;memory.Bytes[getter+1]=0x41;memory.Bytes[getter+2]=0x28;memory.Bytes[getter+3]=0xC3;
        memory.Bytes[label+0x1AD]=1;memory.String(label+0x2A0,"Blue Imp");
        session.Message(Menu);
        memory.Word(Canvas+0x19EEC,0);session.Tick(Menu);
        Assert.DoesNotContain(events.OfType<BattleFocusChanged>(),e=>e.Text?.Contains("Blue Imp",StringComparison.Ordinal)==true);
        Assert.Equal("Blue Imp",Assert.Single(events.OfType<BattleFeedbackPresented>()).Text);
    }

    private sealed class Memory:IReadableMemory
    {
        public byte[] Bytes{get;}=new byte[0x900000];
        public void Word(uint address,uint value)=>BinaryPrimitives.WriteUInt32LittleEndian(Bytes.AsSpan((int)address),value);
        public void String(uint address,string value){var b=Encoding.UTF8.GetBytes(value);Assert.True(b.Length<=15);b.CopyTo(Bytes,(int)address);Word(address+16,(uint)b.Length);Word(address+20,15);}
        public bool TryRead(nuint address,Span<byte> destination){if(address==0 || (ulong)address+(uint)destination.Length>(ulong)Bytes.Length)return false;Bytes.AsSpan((int)address,destination.Length).CopyTo(destination);return true;}
    }
}

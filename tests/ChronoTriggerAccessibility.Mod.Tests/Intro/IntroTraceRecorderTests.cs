using System.Buffers.Binary;
using ChronoTriggerAccessibility.Core.Dialogue;
using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Core.NewGame;
using ChronoTriggerAccessibility.Mod.Intro;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Intro;

public sealed class IntroTraceRecorderTests
{
    [Fact]
    public void RecordsOnlyAfterValidatedStartAndPreservesOriginalExecution()
    {
        var memory = new Memory();
        var log = new List<string>();
        var recorder = new IntroTraceRecorder(memory, log.Add);
        var originals = 0;
        recorder.Enable();
        recorder.Dispatch(0x1000, 0xE3, () => originals++);
        Assert.Equal(0, memory.Reads);
        recorder.Observe(new ModeSelectActivated("Localized start"));

        recorder.Dispatch(0x1000, 0xE3, () => { originals++; memory.Word(0x608C, 1); });

        Assert.Equal(2, originals);
        Assert.Contains(log, line => line.Contains("pc=0x0460") && line.Contains("control=0->1"));
        Assert.True(recorder.IsRecording); // A control edge alone is not proof the intro ended.
    }

    [Fact]
    public void CoalescesRepeatedDispatchesAndRecordsDialogueBoundaries()
    {
        var memory = new Memory();
        var log = new List<string>();
        var recorder = new IntroTraceRecorder(memory, log.Add);
        recorder.Enable();
        recorder.Observe(new ModeSelectActivated("Start"));
        for (var index = 0; index < 20; index++) recorder.Dispatch(0x1000, 0xE3, () => { });
        recorder.Observe(new DialogueOpened());
        recorder.Observe(new DialogueClosed());
        recorder.Observe(new StartupSceneEntered(StartupSceneKind.Title));

        Assert.Single(log, line => line.Contains(" opcode="));
        Assert.Contains(log, line => line.Contains("dialogue=open"));
        Assert.Contains(log, line => line.Contains("dialogue=closed"));
        Assert.False(recorder.IsRecording);
        var reads = memory.Reads;
        recorder.Dispatch(0x1000, 0xE3, () => { });
        Assert.Equal(reads, memory.Reads);
    }

    [Fact]
    public void StopsAtDispatchLimitAndNeverSkipsTheGameCall()
    {
        var memory = new Memory();
        var recorder = new IntroTraceRecorder(memory, _ => { });
        recorder.Enable();
        recorder.Observe(new ModeSelectActivated("Start"));
        var originals = 0;
        for (var index = 0; index < IntroTraceRecorder.MaximumDispatches + 2; index++)
            recorder.Dispatch(0x1000, 0xE3, () => originals++);
        Assert.False(recorder.IsRecording);
        Assert.Equal(IntroTraceRecorder.MaximumDispatches + 2, originals);
    }

    [Fact]
    public void DiagnosticFailuresAndUnreadableStateCannotSkipOriginal()
    {
        var memory = new Memory();
        var recorder = new IntroTraceRecorder(memory, _ => throw new InvalidOperationException("logger"));
        recorder.Enable();
        recorder.Observe(new ModeSelectActivated("Start"));
        var originals = 0;
        for (var i = 0; i < 8; i++) recorder.Dispatch(0, 0xE3, () => originals++);
        Assert.Equal(8, originals);
        Assert.False(recorder.IsRecording);
    }

    [Fact]
    public void RecoversFromTransientPreAndPostDispatchReadFailures()
    {
        var memory = new Memory();
        var log = new List<string>();
        var recorder = new IntroTraceRecorder(memory, log.Add);
        recorder.Enable();
        recorder.Observe(new ModeSelectActivated("Start"));
        var calls = 0;

        recorder.Dispatch(0, 0xE3, () => calls++);
        recorder.Dispatch(0x1000, 0xE3, () => { calls++; memory.Word(0x1850, 0x7000); });
        memory.Word(0x1850, 0x5000);
        recorder.Dispatch(0x1000, 0xE3, () => calls++);

        Assert.Equal(3, calls);
        Assert.True(recorder.IsRecording);
        Assert.Single(log, line => line.Contains("capture-gap"));
        Assert.Contains(log, line => line.Contains("capture-resumed; skipped=2"));
        Assert.Contains(log, line => line.Contains("seq=3;") && line.Contains(" opcode="));
    }

    [Fact]
    public void FlagsAStaleProgramCounterWithoutMisattributingTheOpcode()
    {
        var log = new List<string>();
        var recorder = new IntroTraceRecorder(new Memory(), log.Add);
        recorder.Enable();
        recorder.Observe(new ModeSelectActivated("Start"));

        recorder.Dispatch(0x1000, 0xA0, () => { });
        recorder.Dispatch(0x1000, 0xE3, () => { });

        Assert.Contains(log, line => line.Contains("opcode=0xA0") &&
            line.Contains("bytes=E301001122334455") && line.Contains("pcMatch=False"));
        Assert.Contains(log, line => line.Contains("opcode=0xE3") && line.Contains("pcMatch=True"));
    }

    [Fact]
    public void ReportsAnUnexpectedDispatchThreadOnceAndStillCallsTheGame()
    {
        var memory = new Memory();
        var log = new List<string>();
        var recorder = new IntroTraceRecorder(memory, log.Add);
        recorder.Enable();
        recorder.Observe(new ModeSelectActivated("Start"));
        var calls = 0;

        var worker = new Thread(() =>
        {
            recorder.Dispatch(0x1000, 0xE3, () => calls++);
            recorder.Dispatch(0x1000, 0xE3, () => calls++);
        });
        worker.Start();
        Assert.True(worker.Join(TimeSpan.FromSeconds(5)));

        Assert.Equal(2, calls);
        Assert.Equal(0, memory.Reads);
        Assert.Single(log, line => line.Contains("thread-mismatch;") &&
            line.Contains("owner=") && line.Contains("current="));
        Assert.True(recorder.IsRecording);
    }

    [Fact]
    public async Task LoggingDoesNotHoldTheRecorderLock()
    {
        IntroTraceRecorder? recorder = null;
        Task? disable = null;
        var completedWhileLogging = false;
        recorder = new IntroTraceRecorder(new Memory(), line =>
        {
            if (!line.Contains("started;")) return;
            disable = Task.Run(() => recorder!.Disable());
            completedWhileLogging = disable.Wait(TimeSpan.FromSeconds(2));
        });
        recorder.Enable();
        recorder.Observe(new ModeSelectActivated("Start"));
        Assert.NotNull(disable);
        await disable;

        Assert.True(completedWhileLogging);
        Assert.False(recorder.IsRecording);
    }

    [Fact]
    public void DisableDuringOriginalDiscardsThePendingCapture()
    {
        var memory = new Memory();
        var log = new List<string>();
        var recorder = new IntroTraceRecorder(memory, log.Add);
        recorder.Enable();
        recorder.Observe(new ModeSelectActivated("Start"));
        recorder.Dispatch(0x1000, 0xE3, recorder.Disable);
        Assert.DoesNotContain(log, line => line.Contains(" opcode="));
        Assert.False(recorder.IsRecording);
    }

    [Fact]
    public void NativeExceptionIsNotRetriedAndCannotProduceAPostDispatchRecord()
    {
        var log = new List<string>();
        var recorder = new IntroTraceRecorder(new Memory(), log.Add);
        recorder.Enable();
        recorder.Observe(new ModeSelectActivated("Start"));
        var calls = 0;
        Assert.Throws<InvalidOperationException>(() => recorder.Dispatch(0x1000, 0xE3, () =>
        {
            calls++;
            throw new InvalidOperationException("original");
        }));
        Assert.Equal(1, calls);
        Assert.DoesNotContain(log, line => line.Contains(" opcode="));
        Assert.False(recorder.IsRecording);
    }

    [Fact]
    public void StopsAtTheRecordLimitForDistinctExecutedAddresses()
    {
        var memory = new Memory();
        var log = new List<string>();
        var recorder = new IntroTraceRecorder(memory, log.Add);
        recorder.Enable();
        recorder.Observe(new ModeSelectActivated("Start"));
        var calls = 0;
        for (var i = 0; i < IntroTraceRecorder.MaximumRecords + 2; i++)
        {
            var pc = 0x460u + (uint)i;
            memory.Word(0x1024, pc);
            memory.Add(0xB2001u + pc, Convert.FromHexString("E301001122334455"));
            recorder.Dispatch(0x1000, 0xE3, () => calls++);
        }
        Assert.Equal(IntroTraceRecorder.MaximumRecords, log.Count(line => line.Contains(" opcode=")));
        Assert.Equal(IntroTraceRecorder.MaximumRecords + 2, calls);
        Assert.False(recorder.IsRecording);
    }

    private sealed class Memory : IReadableMemory
    {
        private readonly Dictionary<nuint, byte> bytes = [];
        public int Reads { get; private set; }
        public Memory()
        {
            Word(0x1000, 0xA0000); Word(0x1024, 0x460); Word(0x1850, 0x5000);
            Word(0x1BB4, 4); Word(0x608C, 0);
            Add(0xB2000, Convert.FromHexString("236004A204D504D6"));
            Add(0xB2461, Convert.FromHexString("E301001122334455"));
        }
        public void Word(nuint address, uint value)
        {
            Span<byte> data = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(data, value); Add(address, data);
        }
        public void Add(nuint address, ReadOnlySpan<byte> data)
        {
            for (var i = 0; i < data.Length; i++) bytes[address + (nuint)i] = data[i];
        }
        public bool TryRead(nuint address, Span<byte> destination)
        {
            Reads++;
            for (var i = 0; i < destination.Length; i++)
                if (!bytes.TryGetValue(address + (nuint)i, out destination[i])) return false;
            return true;
        }
    }
}

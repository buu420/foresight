using ChronoTriggerAccessibility.Core.Dialogue;
using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Core.Menus;
using ChronoTriggerAccessibility.Core.NewGame;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Mod.Intro;

public sealed class IntroTraceRecorder(IReadableMemory memory, Action<string> log)
{
    public const int MaximumRecords = 32768;
    public const int MaximumDispatches = 250000;
    private const int MaximumConsecutiveFailures = 8;
    private const int MaximumIdentities = 16;
    private readonly object gate = new();
    private readonly HashSet<Observation> seen = [];
    private readonly HashSet<(uint Context, uint Data, string Prefix)> identities = [];
    private bool enabled;
    private volatile bool recording;
    private bool dialogueOpen;
    private bool threadMismatchReported;
    private int epoch;
    private int ownerThread;
    private int dispatches;
    private int records;
    private int repeated;
    private int consecutiveFailures;
    private int failedCaptures;
    private int mismatchedAddresses;

    public bool IsRecording => recording;
    public void Enable() { lock (gate) { enabled = true; epoch++; } }
    public void Disable()
    {
        List<string>? messages = null;
        lock (gate) { Stop("hooks-disabled", ref messages); enabled = false; epoch++; }
        Write(messages);
    }

    public void Observe(AccessibilityEvent value)
    {
        List<string>? messages = null;
        try
        {
            lock (gate)
            {
                if (!enabled) return;
                if (value is ModeSelectActivated)
                {
                    Stop("new-start", ref messages);
                    epoch++;
                    ownerThread = Environment.CurrentManagedThreadId;
                    dispatches = records = repeated = 0;
                    consecutiveFailures = failedCaptures = mismatchedAddresses = 0;
                    dialogueOpen = threadMismatchReported = false;
                    seen.Clear();
                    identities.Clear();
                    recording = true;
                    Add(ref messages, $"started; owner={ownerThread}; capture only; descriptions are not enabled");
                }
                else if (recording)
                {
                    if (value is DialogueOpened or DialogueClosed)
                    {
                        dialogueOpen = value is DialogueOpened;
                        Add(ref messages, $"seq={dispatches}; dialogue={(dialogueOpen ? "open" : "closed")}");
                    }
                    else if (value is MenuPresented) Stop("menu-presented", ref messages);
                    else if (value is StartupSceneEntered) Stop("startup-scene", ref messages);
                }
            }
        }
        finally { Write(messages); }
    }

    public void Dispatch(nint context, int opcode, Action original)
    {
        ArgumentNullException.ThrowIfNull(original);
        // Inactive gameplay calls do not enter the recorder lock or read memory.
        if (!recording) { original(); return; }
        FieldScriptTraceSnapshot? before = null;
        var capturedEpoch = 0;
        var sequence = 0;
        List<string>? messages = null;
        try
        {
            lock (gate)
            {
                if (enabled && recording)
                {
                    if (ownerThread != Environment.CurrentManagedThreadId)
                    {
                        if (!threadMismatchReported)
                        {
                            threadMismatchReported = true;
                            Add(ref messages, $"thread-mismatch; owner={ownerThread}; " +
                                $"current={Environment.CurrentManagedThreadId}; this thread is not captured");
                        }
                    }
                    else if (dispatches >= MaximumDispatches) Stop("dispatch-limit", ref messages);
                    else
                    {
                        sequence = ++dispatches;
                        capturedEpoch = epoch;
                        if (!FieldScriptTraceCapture.TryCapture(memory, unchecked((nuint)context), out before))
                            CaptureFailed("unreadable-pre-dispatch-state", ref messages);
                    }
                }
            }
        }
        catch (Exception) { lock (gate) CaptureFailed("pre-dispatch-capture-exception", ref messages); }
        finally { Write(messages); }

        // Capturing is diagnostic only. Every path calls the native function once.
        try { original(); }
        catch
        {
            messages = null;
            lock (gate) Stop("native-call-did-not-return", ref messages);
            Write(messages);
            throw;
        }
        if (before is null) return;
        messages = null;
        try
        {
            lock (gate)
            {
                if (!enabled || !recording || capturedEpoch != epoch) return;
                if (!FieldScriptTraceCapture.TryReadControlAfter(memory, before, out var after))
                {
                    CaptureFailed("field-owner-changed-or-unreadable", ref messages);
                    return;
                }
                if (consecutiveFailures > 0)
                {
                    Add(ref messages, $"seq={sequence}; capture-resumed; skipped={consecutiveFailures}");
                    consecutiveFailures = 0;
                }
                var identity = (before.Context, before.Data, before.ScriptPrefix);
                if (!identities.Contains(identity))
                {
                    if (identities.Count >= MaximumIdentities) { Stop("script-identity-limit", ref messages); return; }
                    identities.Add(identity);
                    var hash = FieldScriptTraceCapture.TryHashAtel0000Candidate(memory, before);
                    Add(ref messages, $"script context=0x{before.Context:X8}; data=0x{before.Data:X8}; " +
                        $"header={before.ScriptPrefix}; candidateAtel0000Sha256={hash ?? "unavailable"}");
                }
                var pcMatch = opcode is >= 0 and <= 255 &&
                    before.Bytes.StartsWith($"{opcode:X2}", StringComparison.Ordinal);
                if (!pcMatch) mismatchedAddresses++;
                var observation = new Observation(before, opcode, after, dialogueOpen);
                if (before.ControlBefore == after && seen.Contains(observation))
                {
                    repeated++;
                    return;
                }
                if (records >= MaximumRecords) { Stop("record-limit", ref messages); return; }
                seen.Add(observation);
                records++;
                Add(ref messages, $"seq={sequence}; context=0x{before.Context:X8}; data=0x{before.Data:X8}; " +
                    $"script={before.ScriptId}; actor={before.Actor}; pc=0x{before.Address:X4}; opcode=0x{opcode:X2}; " +
                    $"bytes={before.Bytes}; pcMatch={pcMatch}; " +
                    $"control={before.ControlBefore}->{after}; dialogue={dialogueOpen}");
            }
        }
        catch (Exception) { lock (gate) CaptureFailed("post-dispatch-capture-exception", ref messages); }
        finally { Write(messages); }
    }

    // State and message construction use gate; external logging never does.
    private void CaptureFailed(string reason, ref List<string>? messages)
    {
        if (!recording) return;
        consecutiveFailures++;
        failedCaptures++;
        if (consecutiveFailures == 1)
            Add(ref messages, $"seq={dispatches}; capture-gap; reason={reason}");
        if (consecutiveFailures >= MaximumConsecutiveFailures)
            Stop($"consecutive-capture-failures:{reason}", ref messages);
    }

    private void Stop(string reason, ref List<string>? messages)
    {
        if (!recording) return;
        recording = false;
        Add(ref messages, $"stopped reason={reason}; dispatches={dispatches}; records={records}; " +
            $"repeated={repeated}; failedCaptures={failedCaptures}; mismatchedAddresses={mismatchedAddresses}");
        seen.Clear();
        identities.Clear();
    }

    private void Add(ref List<string>? messages, string text) =>
        (messages ??= []).Add($"Intro trace: run={epoch}; {text}");

    private void Write(List<string>? messages)
    {
        if (messages is null) return;
        foreach (var message in messages)
        {
            try { log(message); }
            catch (Exception) { /* Diagnostics cannot interfere with the game or speech. */ }
        }
    }

    private sealed record Observation(FieldScriptTraceSnapshot Before, int Opcode, uint After, bool Dialogue);
}

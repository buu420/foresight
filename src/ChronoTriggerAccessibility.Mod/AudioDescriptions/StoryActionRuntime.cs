using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Core.Dialogue;
using ChronoTriggerAccessibility.Core.Menus;
using ChronoTriggerAccessibility.Core.Battle;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.AudioDescriptions;

public sealed class StoryActionRuntime
{
    private readonly Func<nint, int, StoryActionSnapshot?> capture;
    private readonly Func<StoryActionSnapshot, StoryAnimationProof?> prove;
    private readonly Func<bool> foreground;
    private readonly Func<int, string?> characterName;
    private readonly Action<string> say;
    private readonly Action<string> diagnostic;
    private readonly Func<bool> assetsReady;
    private readonly Func<StoryActionSnapshot, StorySceneActionCandidate?>? matchScene;
    private readonly Func<StorySceneActionCandidate, bool>? completeScene;
    private readonly Func<StoryActionSnapshot, bool>? controlRestored;
    private readonly object gate = new();
    private readonly HashSet<(int Actor, uint Address, int Animation)> described = [];
    private readonly HashSet<StorySceneAction> describedScenes = [];
    private readonly Queue<string> held = new();
    private string? lastHeld;
    private (uint Context, uint Data, uint Actors, uint Field, int Scene, int Script)? owner;
    private bool enabled;
    private int epoch;
    private uint? lastControl;

    public StoryActionRuntime(Func<nint, int, StoryActionSnapshot?> capture,
        Func<StoryActionSnapshot, StoryAnimationProof?> prove, Func<bool> foreground,
        Func<int, string?> characterName, Action<string> say, Action<string> diagnostic,
        Func<bool>? assetsReady = null,
        Func<StoryActionSnapshot, StorySceneActionCandidate?>? matchScene = null,
        Func<StorySceneActionCandidate, bool>? completeScene = null,
        Func<StoryActionSnapshot, bool>? controlRestored = null)
    {
        this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
        this.prove = prove ?? throw new ArgumentNullException(nameof(prove));
        this.foreground = foreground ?? throw new ArgumentNullException(nameof(foreground));
        this.characterName = characterName ?? throw new ArgumentNullException(nameof(characterName));
        this.say = say ?? throw new ArgumentNullException(nameof(say));
        this.diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
        this.assetsReady = assetsReady ?? (() => true);
        if ((matchScene is null) != (completeScene is null))
            throw new ArgumentException("Scene matching and completion proof must be provided together.");
        this.matchScene = matchScene;
        this.completeScene = completeScene;
        this.controlRestored = controlRestored;
    }

    public void Enable() { lock (gate) { Reset(); enabled = true; } }
    public void Disable() { lock (gate) { enabled = false; Reset(); } }

    public void Observe(AccessibilityEvent value)
    {
        var observedEpoch = -1;
        try
        {
            lock (gate)
            {
                if (!enabled) return;
                observedEpoch = epoch;
                if (!foreground()) { Reset(); return; }
                if (!enabled || observedEpoch != epoch) return;
                switch (value)
                {
                    // Queue before the next line is published. Textbox closure says
                    // nothing about how much speech NVDA still has queued.
                    case DialogueLinePresented or DialogueChoicesPresented:
                        Flush(observedEpoch);
                        break;
                    case DialogueClosed:
                        Flush(observedEpoch);
                        // An ordinary NPC interaction leaves control enabled.
                        // Cutscene textbox pages do not end the scripted episode.
                        if (enabled && epoch == observedEpoch && lastControl == 1) described.Clear();
                        break;
                    case BattleStarted or StartupSceneEntered or ScreenEntered or MenuPresented or MenuContentPresented:
                        Reset();
                        break;
                }
            }
        }
        catch (Exception exception) { Recover(exception, observedEpoch); }
    }

    public void Dispatch(nint context, int opcode, Action original)
    {
        ArgumentNullException.ThrowIfNull(original);
        StoryActionSnapshot? before = null;
        StorySceneActionCandidate? scene = null;
        var observedEpoch = -1;
        // A bounded observer. Other opcodes still run natively, without a field
        // capture or an animation interpretation on each scheduler instruction.
        var interesting = opcode is 0xAA or 0xAB ||
            (controlRestored is not null && opcode == 0xE3) ||
            (matchScene is not null && opcode is 0xE5 or 0xA0 or 0x96 or 0xAC or 0xB7 or 0xAE);
        try
        {
            lock (gate)
            {
                if (enabled)
                {
                    observedEpoch = epoch;
                    if (!foreground()) Reset();
                    else if (interesting && assetsReady() && enabled && observedEpoch == epoch)
                    {
                        before = capture(context, opcode);
                        if (before is not null && enabled && observedEpoch == epoch)
                            scene = matchScene?.Invoke(before);
                    }
                }
            }
        }
        catch (Exception exception) { Recover(exception, observedEpoch); }

        // Neither failed observations nor failed speech may retry or skip this.
        original();

        try
        {
            lock (gate)
            {
                if (!enabled || observedEpoch != epoch) return;
                if (!foreground()) { Reset(); return; }
                if (!enabled || observedEpoch != epoch) return;
                if (opcode is >= 0xDC and <= 0xE1) { Reset(); return; }
                if (observedEpoch != epoch || before is null) return;
                var location = before.Location;
                var nextOwner = (location.Context, location.Data, location.Actors, location.Field, location.Scene, location.ScriptId);
                if (owner != nextOwner)
                {
                    held.Clear(); lastHeld = null; described.Clear(); describedScenes.Clear(); owner = nextOwner;
                }
                lastControl = before.Control;
                // State 0 also covers a textbox that the native engine aborted
                // without publishing a DialogueClosed event.
                if (!before.TextboxOpen) Flush(observedEpoch);
                if (!enabled || observedEpoch != epoch) return;
                if (before.ScriptPause != 0) return;
                if (opcode == 0xE3)
                {
                    // The handler can also disable control; its pre-call word
                    // must not be reused at a subsequent dialogue closure.
                    lastControl = null;
                    if (controlRestored?.Invoke(before) == true && enabled && observedEpoch == epoch)
                    {
                        described.Clear(); lastControl = 1;
                    }
                    return;
                }
                if (scene is not null)
                {
                    // The verified visible subject can differ from the executing
                    // actor, for example the invisible curtain controller.
                    // Exact script digest, instruction, story gate, visible
                    // subjects and native completion identify this scene action.
                    // Scene-load control flags need not be guessed here.
                    if (!completeScene!(scene) || observedEpoch != epoch || !enabled ||
                        describedScenes.Contains(scene.Action)) return;
                    var sceneText = StoryActionCatalog.DescribeScene(scene, characterName);
                    if (observedEpoch != epoch || !enabled) return;
                    describedScenes.Add(scene.Action);
                    Log($"Story scene action: scene={location.Scene}; script={location.ScriptId}; actor={location.Actor}; " +
                        $"pc=0x{location.Address:X}; action={scene.Action}; text={sceneText}");
                    Queue(sceneText, before.TextboxOpen, observedEpoch);
                    return;
                }
                if (!before.ActorState.Drawn || before.ActorState.OnScreen != true) return;
                var proof = prove(before);
                if (observedEpoch != epoch || !enabled) return;
                if (proof is { Kind: StoryAnimationKind.PlayOnceFinished })
                {
                    return;
                }
                if (proof is not { Kind: StoryAnimationKind.Looping or StoryAnimationKind.PlayOnceStarted } applied) return;
                if (applied.Value != before.Operand) return;
                if (applied.Value is 0 or 1)
                {
                    return;
                }
                if (before.Control != 0 && !before.TextboxOpen) return;
                var text = StoryActionCatalog.Describe(before, applied.Value, characterName);
                if (observedEpoch != epoch || !enabled) return;
                if (text is null || !described.Add((location.Actor, location.Address, applied.Value))) return;
                Log($"Story action: scene={location.Scene}; script={location.ScriptId}; actor={location.Actor}; " +
                    $"pc=0x{location.Address:X}; animation=0x{applied.Value:X2}; text={text}");
                Queue(text, before.TextboxOpen, observedEpoch);
            }
        }
        catch (Exception exception) { Recover(exception, observedEpoch); }
    }

    private void Queue(string text, bool textboxOpen, int observedEpoch)
    {
        if (!enabled || observedEpoch != epoch) return;
        if (textboxOpen)
        {
            if (lastHeld != text) held.Enqueue(text);
            lastHeld = text;
        }
        else
        {
            Flush(observedEpoch);
            if (enabled && observedEpoch == epoch) say(text);
        }
    }

    private void Flush(int observedEpoch)
    {
        lastHeld = null;
        while (enabled && epoch == observedEpoch && held.TryDequeue(out var text)) say(text);
    }

    private void Reset()
    {
        epoch++; owner = null; lastControl = null; held.Clear(); lastHeld = null; described.Clear(); describedScenes.Clear();
    }

    private void Recover(Exception exception, int failedEpoch)
    {
        lock (gate) if (epoch == failedEpoch) Reset();
        Log($"Story action observer recovered: {exception.GetType().Name}: {exception.Message}");
    }

    private void Log(string text)
    {
        try { diagnostic(text); }
        catch (Exception) { /* Diagnostic failures cannot escape into native code. */ }
    }
}

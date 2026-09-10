using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Core.State;
using ChronoTriggerAccessibility.Mod.Diagnostics;

namespace ChronoTriggerAccessibility.Mod.Runtime;

public sealed class SemanticEventDispatcher : ISemanticEventDispatcher
{
    private readonly AccessibilityState state = new();
    private readonly IModLog log;
    private readonly IAccessibleFatalError fatalError;
    private readonly object gate = new();
    private readonly HashSet<string> reportedCoverageFailures = new(StringComparer.Ordinal);
    private IRuntimePrismSession? session;
    private bool fatalCoverageWindowShown;

    public SemanticEventDispatcher(IModLog log, IAccessibleFatalError fatalError)
    {
        this.log = log ?? throw new ArgumentNullException(nameof(log));
        this.fatalError = fatalError ?? throw new ArgumentNullException(nameof(fatalError));
    }

    public int Generation
    {
        get
        {
            lock (gate)
            {
                return state.Generation;
            }
        }
    }

    public void Attach(IRuntimePrismSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (gate)
        {
            if (this.session is not null && !ReferenceEquals(this.session, session))
            {
                throw new InvalidOperationException("A different Prism session is already attached.");
            }

            this.session = session;
        }
    }

    public void Detach(IRuntimePrismSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (gate)
        {
            if (ReferenceEquals(this.session, session))
            {
                this.session = null;
            }
        }
    }

    public void Publish(AccessibilityEvent accessibilityEvent)
    {
        ArgumentNullException.ThrowIfNull(accessibilityEvent);
        lock (gate)
        {
            log.Info($"Semantic event: {accessibilityEvent}");
            var announcements = state.Apply(accessibilityEvent);
            foreach (var announcement in announcements)
            {
                var activeSession = session ?? throw new InvalidOperationException(
                    "A semantic announcement was produced without an attached Prism session.");
                activeSession.Output(announcement.Text, announcement.Interrupt);
                log.Info(
                    $"Announcement: interrupt={announcement.Interrupt}; text={announcement.Text}");
            }
        }
    }

    public void RecordDiagnostic(string message)
    {
        try
        {
            log.Info(message);
        }
        catch (Exception)
        {
            // Diagnostic-only output must not change speech or escape to the game.
        }
    }

    public void ReportCoverageFailure(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        var accessibleMessage = $"Chrono Trigger accessibility error: {message}";
        var showFatalWindow = false;
        try
        {
            lock (gate)
            {
                if (!reportedCoverageFailures.Add(accessibleMessage))
                {
                    return;
                }
                if (!fatalCoverageWindowShown)
                {
                    fatalCoverageWindowShown = true;
                    showFatalWindow = true;
                }
                log.Error(accessibleMessage);
                session?.Output(accessibleMessage, interrupt: true);
            }
        }
        catch (Exception exception)
        {
            try
            {
                log.Error($"{accessibleMessage} Prism error: {exception}");
            }
            catch (Exception)
            {
                // Continue to the independent native accessible sink.
            }
        }

        if (!showFatalWindow)
        {
            return;
        }

        try
        {
            fatalError.Show(accessibleMessage);
        }
        catch (Exception)
        {
            // Coverage is already faulted; no diagnostic exception may reach the game.
        }
    }
}

namespace ChronoTriggerAccessibility.Mod.Diagnostics;

public sealed class UnmanagedBoundaryGuard(IModLog log, IAccessibleFatalError fatalError)
{
    private int faulted;

    public bool IsFaulted => Volatile.Read(ref faulted) != 0;

    public void Run(string boundaryName, Action action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boundaryName);
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            action();
        }
        catch (Exception exception)
        {
            Report(boundaryName, exception);
        }
    }

    public T Run<T>(string boundaryName, Func<T> action, T fallback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boundaryName);
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            return action();
        }
        catch (Exception exception)
        {
            Report(boundaryName, exception);
            return fallback;
        }
    }

    private void Report(string boundaryName, Exception exception)
    {
        var message = $"Managed failure in unmanaged boundary '{boundaryName}': {exception}";
        try
        {
            log.Error(message);
        }
        catch (Exception)
        {
            // A diagnostic failure must never escape into native game code.
        }

        if (Interlocked.Exchange(ref faulted, 1) != 0)
        {
            return;
        }

        try
        {
            fatalError.Show(message);
        }
        catch (Exception)
        {
            // The final native error sink is best-effort at an unmanaged boundary.
        }
    }
}

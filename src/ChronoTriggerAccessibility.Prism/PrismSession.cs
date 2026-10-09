namespace ChronoTriggerAccessibility.Prism;

public sealed class PrismException(PrismError error, string message) : InvalidOperationException(message)
{
    public PrismError Error { get; } = error;
}

public sealed class PrismSession : IDisposable
{
    private readonly object sync = new();
    private readonly IPrismNative native;
    private IntPtr context;
    private IntPtr backend;
    private bool disposed;

    public PrismSession()
        : this(new PrismNative())
    {
    }

    internal PrismSession(IPrismNative native)
    {
        this.native = native;
        context = native.Init(IntPtr.Zero);
        if (context == IntPtr.Zero)
        {
            throw new PrismException(PrismError.BackendNotAvailable, native.ErrorString(PrismError.BackendNotAvailable));
        }

        backend = native.CreateBest(context);
        if (backend == IntPtr.Zero)
        {
            native.Shutdown(context);
            context = IntPtr.Zero;
            throw new PrismException(PrismError.BackendNotAvailable, native.ErrorString(PrismError.BackendNotAvailable));
        }

        try
        {
            var backendName = native.BackendName(backend);
            if (string.IsNullOrWhiteSpace(backendName))
            {
                throw new PrismException(
                    PrismError.BackendNotAvailable,
                    "Prism created a backend but did not return its name.");
            }

            BackendName = backendName;
        }
        catch
        {
            native.Free(backend);
            backend = IntPtr.Zero;
            native.Shutdown(context);
            context = IntPtr.Zero;
            throw;
        }
    }

    public string BackendName { get; } = string.Empty;

    public void Output(PrismOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        lock (sync)
        {
            ThrowIfDisposed();
            var error = native.Output(backend, output.Text, output.Interrupt);
            if (error != PrismError.Ok)
            {
                throw new PrismException(error, native.ErrorString(error));
            }
        }
    }

    public void Stop()
    {
        lock (sync)
        {
            ThrowIfDisposed();
            var error = native.Stop(backend);
            if (error != PrismError.Ok) throw new PrismException(error, native.ErrorString(error));
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            if (backend != IntPtr.Zero)
            {
                native.Free(backend);
                backend = IntPtr.Zero;
            }

            if (context != IntPtr.Zero)
            {
                native.Shutdown(context);
                context = IntPtr.Zero;
            }

            disposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}

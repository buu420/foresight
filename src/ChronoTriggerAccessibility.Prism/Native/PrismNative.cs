using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ChronoTriggerAccessibility.Prism;

public enum PrismError
{
    Ok = 0,
    SpeakFailure = 6,
    BackendNotAvailable = 16,
}

internal interface IPrismNative
{
    IntPtr Init(IntPtr configuration);
    IntPtr CreateBest(IntPtr context);
    PrismError Output(IntPtr backend, string text, bool interrupt);
    PrismError Stop(IntPtr backend);
    string? BackendName(IntPtr backend);
    void Free(IntPtr backend);
    void Shutdown(IntPtr context);
    string ErrorString(PrismError error);
}

internal sealed partial class PrismNative : IPrismNative
{
    static PrismNative()
    {
        PrismLibraryResolver.Install(typeof(PrismNative).Assembly);
    }

    public IntPtr Init(IntPtr configuration) => PrismInit(configuration);

    public IntPtr CreateBest(IntPtr context) => PrismRegistryCreateBest(context);

    public PrismError Output(IntPtr backend, string text, bool interrupt) => PrismBackendOutput(backend, text, interrupt ? 1 : 0);
    public PrismError Stop(IntPtr backend) => PrismBackendStop(backend);

    public string? BackendName(IntPtr backend) => Marshal.PtrToStringUTF8(PrismBackendName(backend));

    public void Free(IntPtr backend) => PrismBackendFree(backend);

    public void Shutdown(IntPtr context) => PrismShutdown(context);

    public string ErrorString(PrismError error)
    {
        var value = PrismErrorString(error);
        return Marshal.PtrToStringUTF8(value) ?? error.ToString();
    }

    [LibraryImport("prism", EntryPoint = "prism_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial IntPtr PrismInit(IntPtr configuration);

    [LibraryImport("prism", EntryPoint = "prism_shutdown")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void PrismShutdown(IntPtr context);

    [LibraryImport("prism", EntryPoint = "prism_registry_create_best")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial IntPtr PrismRegistryCreateBest(IntPtr context);

    [LibraryImport("prism", EntryPoint = "prism_backend_output", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial PrismError PrismBackendOutput(IntPtr backend, string text, int interrupt);

    [LibraryImport("prism", EntryPoint = "prism_backend_stop")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial PrismError PrismBackendStop(IntPtr backend);

    [LibraryImport("prism", EntryPoint = "prism_backend_name")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial IntPtr PrismBackendName(IntPtr backend);

    [LibraryImport("prism", EntryPoint = "prism_backend_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void PrismBackendFree(IntPtr backend);

    [LibraryImport("prism", EntryPoint = "prism_error_string")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial IntPtr PrismErrorString(PrismError error);
}

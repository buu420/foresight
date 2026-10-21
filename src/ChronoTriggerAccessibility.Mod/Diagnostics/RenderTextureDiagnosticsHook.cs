using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Native.Memory;
using SharedReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace ChronoTriggerAccessibility.Mod.Diagnostics;

/// <summary>Observes the audited two-argument CDECL factory. The original result and native
/// allocation/control flow are preserved, including null. This is not null recovery.</summary>
public sealed class RenderTextureDiagnosticsHook(SharedReloadedHooks hooks, string gameRoot,
    string version, Action<string> diagnostic) : IHookRegistration
{
    public const string LibrarySha256 = "8A53EF5BFFD345D4EEE0E2D91152C539FCEB71725DB44A72D2E7513DF55ECD2F";
    public const uint CreateRva = 0x2CC31E;
    private ReloadedPreparedHook<RenderTextureCreateDelegate>? prepared;
    public string Name => "Failed render-texture diagnostics";

    public IPreparedHook Prepare(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary)
    {
        try
        {
            if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
                throw new PlatformNotSupportedException("Texture diagnostics require the audited x86 process.");
            var module = GetModuleHandleW("libcocos2d.dll");
            var path = new StringBuilder(32768);
            var length = module == 0 ? 0 : GetModuleFileNameW(module, path, path.Capacity);
            if (length == 0 || length >= path.Capacity)
                throw new InvalidOperationException("The texture library is not available.");
            using (var file = File.OpenRead(path.ToString()))
                if (Convert.ToHexString(SHA256.HashData(file)) != LibrarySha256)
                    throw new InvalidOperationException("The texture library does not match the audited build.");
            var memory = new CurrentProcessReadableMemory();
            var imageBase = unchecked((uint)module);
            if (!EntryMatches(memory, imageBase))
                throw new InvalidOperationException("The texture factory entry does not match the audited CDECL function.");

            var log = new RenderTextureFailureLog(gameRoot, version, diagnostic);
            var observer = new RenderTextureFailureObserver(
                () => RenderTextureStateCapture.Capture(memory, imageBase), TextureMemoryCapture.Capture, log.Record);
            RenderTextureCreateDelegate detour = (width, height) => observer.Create(width, height, prepared!.OriginalFunction);
            prepared = new(Name, hooks.CreateHook(detour, (long)(imageBase + CreateRva)), detour);
            return new OptionalTextureDiagnosticHook(prepared, log.Ready, Report);
        }
        catch (Exception exception)
        {
            Report(exception.Message);
            return new UnavailableHook(Name);
        }
    }

    public static bool EntryMatches(IReadableMemory memory, uint imageBase)
    {
        if (imageBase == 0 || imageBase > uint.MaxValue - 0x87D480) return false;
        Span<byte> expected = stackalloc byte[] { 0x6A, 0x04, 0xB8, 0, 0, 0, 0, 0xE8, 0xD8, 0xAD, 0x14, 0 };
        // MOV EAX, relocated __EHhandler data; the remaining bytes are relocation-free.
        BinaryPrimitives.WriteUInt32LittleEndian(expected[3..7], imageBase + 0x4861FC);
        Span<byte> actual = stackalloc byte[expected.Length];
        return memory.TryRead(imageBase + CreateRva, actual) && actual.SequenceEqual(expected);
    }

    private void Report(string text)
    {
        try { diagnostic("Texture diagnostics unavailable: " + text); }
        catch (Exception) { /* Optional diagnostics cannot fault the accessibility runtime. */ }
    }

    private sealed class UnavailableHook(string name) : IOptionalPreparedHook
    {
        public string Name => name;
        public bool IsActive => false;
        public IReadOnlyCollection<object> LifetimeRoots => [];
        public void Activate() { }
        public void Disable() { }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint GetModuleHandleW(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint GetModuleFileNameW(nint module, StringBuilder name, int size);
}

/// <summary>Includes the original hook and delegate in the normal installer lifetime/rollback.
/// Failure to disable must propagate so a possibly active callback remains rooted.</summary>
public sealed class OptionalTextureDiagnosticHook(IPreparedHook inner, Action ready, Action<string> unavailable)
    : IOptionalPreparedHook
{
    public string Name => inner.Name;
    public bool IsActive => inner.IsActive;
    public IReadOnlyCollection<object> LifetimeRoots => [inner, .. inner.LifetimeRoots];
    public void Activate()
    {
        try { inner.Activate(); }
        catch (Exception exception)
        {
            inner.Disable();
            if (inner.IsActive) throw new InvalidOperationException("Texture diagnostic detour could not be disabled.", exception);
            try { unavailable(exception.Message); } catch (Exception) { }
            return;
        }
        try { ready(); } catch (Exception) { /* No diagnostic sink may affect the game. */ }
    }
    public void Disable() => inner.Disable();
}

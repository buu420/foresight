using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using Reloaded.Hooks.Definitions.X86;
using SharedReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace ChronoTriggerAccessibility.Mod.AudioDescriptions;

[Function(CallingConventions.Cdecl)]
public delegate int MovieTopologyDelegate(nint source, nint descriptor, nint window, nint topologyOut);

/// <summary>The native player handles synchronization, pause, seek, skip and teardown
/// of both audio renderers using its existing IMFMediaSession.</summary>
public sealed class MovieAudioTracksHook(SharedReloadedHooks hooks, string gameRoot, Action<string> diagnostic,
    Action<string> unavailable)
    : IHookRegistration
{
    public const string LibrarySha256 = "8A53EF5BFFD345D4EEE0E2D91152C539FCEB71725DB44A72D2E7513DF55ECD2F";
    public const uint TopologyRva = 0x287578;
    private ReloadedPreparedHook<MovieTopologyDelegate>? prepared;
    private readonly MovieAudioPlayback playback = new(diagnostic, unavailable);
    public string Name => "Movie simultaneous soundtrack and narration";
    public bool OpeningReady => prepared?.IsActive == true && playback.OpeningReady;

    public IPreparedHook Prepare(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary)
    {
        if (!InstalledMovieDescriptions.HasSeparatePack(gameRoot)) return new UnavailableHook(Name);
        try { return PrepareSupportedPlayer(); }
        catch (Exception exception)
        {
            playback.Report(exception.Message);
            return new UnavailableHook(Name);
        }
    }

    private IPreparedHook PrepareSupportedPlayer()
    {
        // This function is in the separately audited engine DLL, not the EXE.
        var module = GetModuleHandleW("libcocos2d.dll");
        var path = new StringBuilder(32768);
        if (module == 0 || GetModuleFileNameW(module, path, path.Capacity) == 0)
            throw new InvalidOperationException("The verified movie player library is not loaded.");
        using (var file = File.OpenRead(path.ToString()))
            if (Convert.ToHexString(SHA256.HashData(file)) != LibrarySha256)
                throw new InvalidOperationException("The movie player library does not match the audited build.");
        var address = module + (int)TopologyRva;
        byte[] bytes = new byte[5];
        Marshal.Copy(address, bytes, 0, bytes.Length);
        if (!bytes.AsSpan().SequenceEqual(Convert.FromHexString("558BEC5151")))
            throw new InvalidOperationException("The movie topology entry bytes do not match the audited function.");
        var temp = new StringBuilder(32768);
        var length = GetTempPathW(temp.Capacity, temp);
        if (length == 0 || length >= temp.Capacity) throw new IOException("Cannot locate the native movie temporary file.");
        // VideoPlayer constructor RVA 0x286D9E uses GetTempPathA + "tmp.mp4".
        var decodedMovie = Path.Combine(temp.ToString(), "tmp.mp4");
        MovieTopologyDelegate detour = (source, descriptor, window, topologyOut) => playback.Build(
            () => descriptor == 0 ? throw new InvalidDataException("Missing movie presentation.") : new NativeMoviePresentation(descriptor),
            () => InstalledMovieDescriptions.MatchSeparateMovie(gameRoot, decodedMovie),
            () => prepared!.OriginalFunction(source, descriptor, window, topologyOut));
        var hook = hooks.CreateHook(detour, (long)address);
        prepared = new ReloadedPreparedHook<MovieTopologyDelegate>(Name, hook, detour);
        return new OptionalMovieHook(prepared, playback.Report);
    }

    private sealed class OptionalMovieHook(IPreparedHook inner, Action<string> report) : IOptionalPreparedHook
    {
        public string Name => inner.Name;
        public bool IsActive => inner.IsActive;
        public IReadOnlyCollection<object> LifetimeRoots => [inner, .. inner.LifetimeRoots];
        public void Activate()
        {
            try { inner.Activate(); }
            catch (Exception exception)
            {
                // A recoverable optional activation failure should not disable menus.
                // Cleanup failure still propagates: a possibly live detour must stay rooted.
                inner.Disable();
                if (inner.IsActive) throw new InvalidOperationException("Movie detour could not be disabled.", exception);
                report(exception.Message);
            }
        }
        public void Disable() => inner.Disable();
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
    private static extern uint GetModuleFileNameW(nint module, StringBuilder fileName, int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint GetTempPathW(int length, StringBuilder buffer);
}

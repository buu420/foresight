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

[Function(CallingConventions.MicrosoftThiscall)]
public delegate int MovieSessionCloseDelegate(nint player);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate int MovieSessionEventDelegate(nint player, nint mediaEvent);

/// <summary>The native player handles synchronization, pause, seek, skip and teardown
/// of every audio renderer using its existing IMFMediaSession.</summary>
public sealed class MovieAudioTracksHook(SharedReloadedHooks hooks, string gameRoot, Action<string> diagnostic,
    Action<string> unavailable, Func<IReadOnlyList<MovieNarrationTrack>>? narration = null)
    : IHookRegistration
{
    public const string LibrarySha256 = "8A53EF5BFFD345D4EEE0E2D91152C539FCEB71725DB44A72D2E7513DF55ECD2F";
    public const uint TopologyRva = 0x287578;
    // CPlayer-derived CloseSession: Close, wait for MESessionClosed, source and session Shutdown.
    public const uint SessionCloseRva = 0x287361;
    // m_pSource, written by OpenURL (RVA 0x2878D8) and passed to the topology builder.
    public const int PlayerSourceOffset = 0xC;
    // HandleEvent(IMFMediaEvent*), RET 4: dispatches successful events and releases the event.
    public const uint SessionEventRva = 0x28762D;
    private ReloadedPreparedHook<MovieTopologyDelegate>? prepared;
    private ReloadedPreparedHook<MovieSessionCloseDelegate>? closing;
    private ReloadedPreparedHook<MovieSessionEventDelegate>? events;
    private readonly MovieAudioPlayback playback = new(diagnostic, unavailable);
    private readonly MovieNarrationLifetime lifetime = new(diagnostic, unavailable);
    public string Name => "Movie simultaneous soundtrack and narration";
    public bool OpeningReady => prepared?.IsActive == true && playback.OpeningReady;
    /// <summary>Known non-opening movies (injected originals and separate-pack movies) from their native
    /// topology build until the player's own CloseSession returns, or Reset when the hook is disabled.</summary>
    public IMovieLifecycle Movies => lifetime;

    public IPreparedHook Prepare(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary)
    {
        var separate = InstalledMovieDescriptions.HasSeparatePack(gameRoot);
        var tracks = LoadNarration();
        if (!separate && tracks.Count == 0) return new UnavailableHook(Name);
        try { return PrepareSupportedPlayer(separate, tracks); }
        catch (Exception exception)
        {
            if (separate) playback.Report(exception.Message);
            if (tracks.Count > 0) ReportNarration(exception.Message);
            return new UnavailableHook(Name);
        }
    }

    private IReadOnlyList<MovieNarrationTrack> LoadNarration()
    {
        try { return (narration ?? (() => MovieNarrationPack.Load()))(); }
        catch (Exception exception)
        {
            ReportNarration(exception.Message);
            return [];
        }
    }

    private IPreparedHook PrepareSupportedPlayer(bool separate, IReadOnlyList<MovieNarrationTrack> tracks)
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
        if (!EntryMatches(address, "558BEC5151"))
            throw new InvalidOperationException("The movie topology entry bytes do not match the audited function.");
        var temp = new StringBuilder(32768);
        var length = GetTempPathW(temp.Capacity, temp);
        if (length == 0 || length >= temp.Capacity) throw new IOException("Cannot locate the native movie temporary file.");
        // VideoPlayer constructor RVA 0x286D9E uses GetTempPathA + "tmp.mp4".
        var decodedMovie = Path.Combine(temp.ToString(), "tmp.mp4");
        try { closing = PrepareSessionClose(module); }
        // The separate pack still works when only the session lifetime cannot be verified.
        catch (Exception exception) when (separate)
        {
            if (tracks.Count > 0) ReportNarration(exception.Message);
            else diagnostic("Movie lifecycle unavailable: " + exception.Message);
        }
        // Failure observation needs the close hook, which starts each player's next session.
        if (closing is not null)
        {
            try { events = PrepareSessionEvents(module); }
            catch (Exception exception) { diagnostic("Movie playback failure observation unavailable: " + exception.Message); }
        }
        var injector = closing is not null && tracks.Count > 0
            ? new MovieNarrationInjector(tracks, new NativeMovieNarrationMedia(), lifetime, () => closing?.IsActive == true,
                diagnostic, unavailable)
            : null;
        MovieTopologyDelegate detour = (source, descriptor, window, topologyOut) => playback.Build(
            () => descriptor == 0 ? throw new InvalidDataException("Missing movie presentation.") : new NativeMoviePresentation(descriptor),
            () => InstalledMovieDescriptions.MatchSeparateMovie(gameRoot, decodedMovie),
            () => prepared!.OriginalFunction(source, descriptor, window, topologyOut),
            injector is null ? null : presentation => injector.TryBuild(presentation, source, decodedMovie,
                (narrated, narratedDescriptor) => prepared!.OriginalFunction(narrated, narratedDescriptor, window, topologyOut)),
            () => injector?.IdentifiedMovie,
            // Without the active close hook no end could arrive, so no start is reported.
            (movie, narrated) => { if (closing?.IsActive == true) lifetime.Begin(source, movie, narrated); });
        var hook = hooks.CreateHook(detour, (long)address);
        prepared = new ReloadedPreparedHook<MovieTopologyDelegate>(Name, hook, detour);
        // The lifetime hook activates first and disables last, so narration is never built without it.
        IPreparedHook[] order = closing is null ? [prepared] : events is null ? [closing, prepared] : [closing, events, prepared];
        return new OptionalMovieHook(order, lifetime.Reset, detail =>
        {
            if (separate) playback.Report(detail);
            if (injector is not null) ReportNarration(detail);
        });
    }

    private ReloadedPreparedHook<MovieSessionCloseDelegate> PrepareSessionClose(nint module)
    {
        var address = module + (int)SessionCloseRva;
        // push ebx; push esi; mov esi,ecx; xor ebx,ebx; push edi; lea eax,[esi+10h]; push eax
        if (!EntryMatches(address, "53568BF133DB578D461050"))
            throw new InvalidOperationException("The movie session close entry bytes do not match the audited function.");
        MovieSessionCloseDelegate detour = player => lifetime.CloseSession(player,
            target => Marshal.ReadIntPtr(target, PlayerSourceOffset), target => closing!.OriginalFunction(target));
        return new(Name + " session lifetime", hooks.CreateHook(detour, (long)address), detour);
    }

    private ReloadedPreparedHook<MovieSessionEventDelegate> PrepareSessionEvents(nint module)
    {
        var address = module + (int)SessionEventRva;
        // push ebp; mov ebp,esp; push ecx; push ecx (relocation-free)
        if (!EntryMatches(address, "558BEC5151"))
            throw new InvalidOperationException("The movie event handler entry bytes do not match the audited function.");
        MovieSessionEventDelegate detour = (player, mediaEvent) => lifetime.HandleEvent(player, mediaEvent,
            NativeMovieEvent.Failure, (target, value) => events!.OriginalFunction(target, value));
        return new(Name + " playback failure", hooks.CreateHook(detour, (long)address), detour);
    }

    private static bool EntryMatches(nint address, string hex)
    {
        var expected = Convert.FromHexString(hex);
        var bytes = new byte[expected.Length];
        Marshal.Copy(address, bytes, 0, bytes.Length);
        return bytes.AsSpan().SequenceEqual(expected);
    }

    private void ReportNarration(string detail)
    {
        try { diagnostic("Movie narration unavailable: " + detail); }
        catch (Exception) { /* Diagnostics must not escape into game code. */ }
        try { unavailable(MovieNarrationInjector.UnavailableMessage); }
        catch (Exception) { /* Diagnostics must not escape into game code. */ }
    }

    private sealed class OptionalMovieHook(IReadOnlyList<IPreparedHook> inner, Action reset, Action<string> report)
        : IOptionalPreparedHook
    {
        public string Name => inner[^1].Name;
        public bool IsActive => inner.Any(hook => hook.IsActive);
        public IReadOnlyCollection<object> LifetimeRoots => [.. inner, .. inner.SelectMany(hook => hook.LifetimeRoots)];
        public void Activate()
        {
            try
            {
                foreach (var hook in inner) hook.Activate();
            }
            catch (Exception exception)
            {
                // A recoverable optional activation failure should not disable menus.
                // Cleanup failure still propagates: a possibly live detour must stay rooted.
                Disable();
                if (IsActive) throw new InvalidOperationException("Movie detour could not be disabled.", exception);
                report(exception.Message);
            }
        }

        public void Disable()
        {
            Exception? failure = null;
            for (var index = inner.Count - 1; index >= 0; index--)
            {
                try { inner[index].Disable(); }
                catch (Exception exception) { failure ??= exception; }
            }
            // No native close can follow; activity ends but sources a session may use are kept.
            reset();
            if (failure is not null) throw new InvalidOperationException("Movie detour disable failed.", failure);
        }
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

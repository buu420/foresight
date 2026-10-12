using System.Runtime.InteropServices;

namespace ChronoTriggerAccessibility.Mod.AudioDescriptions;

/// <summary>SDK COM slots (mfidl.h, mfobjects.h), not game object offsets. Runs inside the game's
/// topology builder call, after the player's own MFStartup and source resolution.</summary>
public sealed unsafe class NativeMovieNarrationMedia : IMovieNarrationMedia
{
    private static readonly Guid MediaSource = new("279a808d-aec7-40c8-9c6b-a6b492c78a66");
    private static readonly Guid Attributes = new("2cd2d921-c447-44a7-a13c-4adabfc247e3");
    private static readonly Guid ContentType = new("fc358289-3cb6-460c-a424-b6681260375a");
    private const uint ResolveMediaSource = 0x1, ResolveRead = 0x10000;

    public INarratedMovieSource Open(string decodedMovie, MovieNarrationTrack track)
    {
        nint narration = 0, movie = 0, aggregate = 0, descriptor = 0;
        try
        {
            narration = WaveSource(track.Wave);
            // A second instance of the verified file: aggregating the player's own source would
            // subscribe to its events and make the original topology impossible to restore.
            movie = Resolve(resolver => Call(resolver, 3, decodedMovie, ResolveMediaSource));
            aggregate = Aggregate(movie, narration);
            Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(aggregate, 8))(aggregate, &descriptor));
            return new NarratedMovieSource(aggregate, descriptor, movie, narration);
        }
        catch
        {
            NarratedMovieSource.Close(aggregate, descriptor, movie, narration);
            throw;
        }
    }

    private static nint WaveSource(byte[] wave)
    {
        var global = GlobalAlloc(0x0002, (nuint)wave.Length);
        if (global == 0) throw new OutOfMemoryException("Cannot allocate the movie narration stream.");
        nint stream = 0, byteStream = 0, attributes = 0;
        try
        {
            var target = GlobalLock(global);
            if (target == 0) throw new OutOfMemoryException("Cannot lock the movie narration stream.");
            Marshal.Copy(wave, 0, target, wave.Length);
            GlobalUnlock(global);
            Check(CreateStreamOnHGlobal(global, true, out stream));
            global = 0;
            Check(MFCreateMFByteStreamOnStream(stream, out byteStream));
            // The URL hint and content type select the system WAVE byte stream handler.
            var attributesId = Attributes;
            if (Marshal.QueryInterface(byteStream, in attributesId, out attributes) >= 0)
            {
                var key = ContentType;
                fixed (char* type = "audio/wav")
                    Check(((delegate* unmanaged[Stdcall]<nint, Guid*, char*, int>)Slot(attributes, 25))(attributes, &key, type));
            }
            var bytes = byteStream;
            return Resolve(resolver =>
            {
                nint unknown = 0;
                uint kind;
                fixed (char* url = "narration.wav")
                    Check(((delegate* unmanaged[Stdcall]<nint, nint, char*, uint, nint, uint*, nint*, int>)Slot(resolver, 4))(
                        resolver, bytes, url, ResolveMediaSource | ResolveRead, 0, &kind, &unknown));
                return unknown;
            });
        }
        finally
        {
            if (global != 0) GlobalFree(global);
            Release(attributes);
            Release(byteStream);
            Release(stream);
        }
    }

    private static nint Call(nint resolver, int slot, string url, uint flags)
    {
        nint unknown = 0;
        uint kind;
        fixed (char* path = url)
            Check(((delegate* unmanaged[Stdcall]<nint, char*, uint, nint, uint*, nint*, int>)Slot(resolver, slot))(
                resolver, path, flags, 0, &kind, &unknown));
        return unknown;
    }

    private static nint Resolve(Func<nint, nint> create)
    {
        nint resolver = 0, unknown = 0;
        try
        {
            Check(MFCreateSourceResolver(out resolver));
            unknown = create(resolver);
            var id = MediaSource;
            Check(Marshal.QueryInterface(unknown, in id, out var source));
            return source;
        }
        finally
        {
            Release(unknown);
            Release(resolver);
        }
    }

    private static nint Aggregate(nint movie, nint narration)
    {
        nint collection = 0;
        try
        {
            Check(MFCreateCollection(out collection));
            var add = (delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(collection, 5);
            Check(add(collection, movie));
            Check(add(collection, narration));
            Check(MFCreateAggregateSource(collection, out var aggregate));
            return aggregate;
        }
        finally { Release(collection); }
    }

    private static nint Slot(nint instance, int index) => ((nint*)*(nint*)instance)[index];
    private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr, -1); }

    private static void Release(nint instance)
    {
        if (instance != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(instance, 2))(instance);
    }

    private sealed class NarratedMovieSource(nint source, nint descriptor, nint movie, nint narration) : INarratedMovieSource
    {
        private readonly NativeMoviePresentation presentation = new(descriptor);
        private bool closed;
        public nint Source => source;
        public nint Descriptor => descriptor;
        public IMoviePresentation Presentation => presentation;

        public void Close()
        {
            if (closed) return;
            closed = true;
            Close(source, descriptor, movie, narration);
        }

        // Shutdown before release, as Media Foundation requires for sources the application created.
        // The aggregate also shuts down its sub-sources; a repeated Shutdown returns MF_E_SHUTDOWN.
        public static void Close(nint aggregate, nint descriptor, nint movie, nint narration)
        {
            foreach (var instance in new[] { aggregate, movie, narration })
                if (instance != 0) ((delegate* unmanaged[Stdcall]<nint, int>)Slot(instance, 12))(instance);
            Release(descriptor);
            Release(aggregate);
            Release(movie);
            Release(narration);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(nint memory);
    [DllImport("kernel32.dll")] private static extern nint GlobalFree(nint memory);
    [DllImport("ole32.dll")] private static extern int CreateStreamOnHGlobal(nint memory,
        [MarshalAs(UnmanagedType.Bool)] bool deleteOnRelease, out nint stream);
    [DllImport("mfplat.dll")] private static extern int MFCreateMFByteStreamOnStream(nint stream, out nint byteStream);
    [DllImport("mfplat.dll")] private static extern int MFCreateSourceResolver(out nint resolver);
    [DllImport("mfplat.dll")] private static extern int MFCreateCollection(out nint collection);
    [DllImport("mf.dll")] private static extern int MFCreateAggregateSource(nint collection, out nint source);
}

/// <summary>Reads an IMFMediaEvent without changing its reference count (mfobjects.h slots:
/// GetType 33, GetStatus 35). The player's handler owns and releases the event.</summary>
public static unsafe class NativeMovieEvent
{
    public static int? Failure(nint mediaEvent)
    {
        var table = (nint*)*(nint*)mediaEvent;
        uint type;
        var hr = ((delegate* unmanaged[Stdcall]<nint, uint*, int>)table[33])(mediaEvent, &type);
        if (hr < 0) return hr;
        int status;
        hr = ((delegate* unmanaged[Stdcall]<nint, int*, int>)table[35])(mediaEvent, &status);
        if (hr < 0) return hr;
        return status < 0 ? status : null;
    }
}

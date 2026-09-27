using System.Runtime.InteropServices;

namespace ChronoTriggerAccessibility.Mod.AudioDescriptions;

public enum MovieStreamKind { Other, Audio, Video }

public interface IMoviePresentation
{
    int Count { get; }
    (MovieStreamKind Kind, bool Selected) GetStream(int index);
    void Select(int index, bool selected);
}

public static class MovieAudioTracks
{
    // Only called for a hash-verified pack. Keep the native video branch and let
    // the game's topology builder create an independent renderer for each audio.
    public static bool SelectTogether(IMoviePresentation presentation)
    {
        if (presentation.Count != 3) return false;
        var streams = Enumerable.Range(0, 3).Select(presentation.GetStream).ToArray();
        if (streams.Count(s => s.Kind == MovieStreamKind.Audio) != 2 ||
            streams.Count(s => s.Kind == MovieStreamKind.Video && s.Selected) != 1) return false;
        var changed = new List<int>();
        try
        {
            for (var i = 0; i < streams.Length; i++)
            {
                if (streams[i].Kind != MovieStreamKind.Audio || streams[i].Selected) continue;
                // Record before the call: a failed provider may still have changed state.
                changed.Add(i);
                presentation.Select(i, true);
            }
            return true;
        }
        catch
        {
            foreach (var index in changed.AsEnumerable().Reverse()) presentation.Select(index, false);
            throw;
        }
    }
}

/// <summary>SDK COM slots, not game object offsets. The game supplies this live
/// IMFPresentationDescriptor directly to its audited topology builder.</summary>
public sealed unsafe class NativeMoviePresentation(nint descriptor) : IMoviePresentation
{
    private static readonly Guid Audio = new("73647561-0000-0010-8000-00aa00389b71");
    private static readonly Guid Video = new("73646976-0000-0010-8000-00aa00389b71");
    private static nint Slot(nint instance, int index) => ((nint*)*(nint*)instance)[index];
    private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
    private static void Release(nint instance)
    {
        if (instance != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(instance, 2))(instance);
    }

    public int Count
    {
        get
        {
            uint count = 0;
            Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)Slot(descriptor, 33))(descriptor, &count));
            return checked((int)count);
        }
    }

    public (MovieStreamKind Kind, bool Selected) GetStream(int index)
    {
        int selected = 0;
        nint stream = 0, handler = 0;
        try
        {
            Check(((delegate* unmanaged[Stdcall]<nint, uint, int*, nint*, int>)Slot(descriptor, 34))(
                descriptor, checked((uint)index), &selected, &stream));
            Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(stream, 34))(stream, &handler));
            Guid type = default;
            Check(((delegate* unmanaged[Stdcall]<nint, Guid*, int>)Slot(handler, 8))(handler, &type));
            return (type == Audio ? MovieStreamKind.Audio : type == Video ? MovieStreamKind.Video : MovieStreamKind.Other,
                selected != 0);
        }
        finally { Release(handler); Release(stream); }
    }

    public void Select(int index, bool selected) =>
        Check(((delegate* unmanaged[Stdcall]<nint, uint, int>)Slot(descriptor, selected ? 35 : 36))(
            descriptor, checked((uint)index)));
}

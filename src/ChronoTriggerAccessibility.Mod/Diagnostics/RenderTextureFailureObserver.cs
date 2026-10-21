using System.Buffers.Binary;
using System.Numerics;
using ChronoTriggerAccessibility.Native.Memory;
using Reloaded.Hooks.Definitions.X86;

namespace ChronoTriggerAccessibility.Mod.Diagnostics;

[Function(CallingConventions.Cdecl)]
public delegate nint RenderTextureCreateDelegate(int width, int height);

public sealed record RenderTextureState(float? ContentScaleFactor, uint? ContentScaleBits,
    bool? SupportsNpot, string? Error);

public sealed record RenderTextureAllocationEstimate(uint? PixelWidth, uint? PixelHeight,
    ulong? RgbaBytes, uint? NativeByteCount32, string? Error);

public sealed record RenderTextureFailure(int Width, int Height, RenderTextureState State,
    RenderTextureAllocationEstimate Estimate, TextureMemorySnapshot Memory);

public sealed class RenderTextureFailureObserver(Func<RenderTextureState> captureState,
    Func<TextureMemorySnapshot> captureMemory, Action<RenderTextureFailure> report)
{
    public nint Create(int width, int height, RenderTextureCreateDelegate original)
    {
        // Never catch/retry the original or turn its failure into a different pointer.
        var result = original(width, height);
        if (result != 0) return result;
        try
        {
            RenderTextureState state;
            try { state = captureState(); }
            catch (Exception) { state = new(null, null, null, "Native scalar capture failed."); }
            TextureMemorySnapshot memory;
            try { memory = captureMemory(); }
            catch (Exception) { memory = new(null, null, null, null, null, null, "Memory capture failed."); }
            report(new(width, height, state, RenderTextureStateCapture.Estimate(width, height, state), memory));
        }
        catch (Exception) { /* A diagnostic failure cannot escape to the native caller. */ }
        return result;
    }
}

public static class RenderTextureStateCapture
{
    public const uint DirectorPointerRva = 0x87D47C;
    public const uint ConfigurationPointerRva = 0x87CBFC;
    public const uint DirectorScaleOffset = 0xE0;
    public const uint ConfigurationNpotOffset = 0x24;

    public static RenderTextureState Capture(IReadableMemory memory, nuint libraryBase)
    {
        // Read existing objects only. The exported getInstance functions allocate when null.
        var errors = new List<string>();
        var bits = ReadField(memory, libraryBase, DirectorPointerRva, DirectorScaleOffset, 4);
        if (bits is null) errors.Add("Director content scale unavailable.");
        var npot = ReadField(memory, libraryBase, ConfigurationPointerRva, ConfigurationNpotOffset, 1);
        if (npot is null) errors.Add("Configuration NPOT flag unavailable.");
        return new(bits is uint value ? BitConverter.UInt32BitsToSingle(value) : null, bits,
            npot is uint flag ? flag != 0 : null, errors.Count == 0 ? null : string.Join(" ", errors));
    }

    private static uint? ReadField(IReadableMemory memory, nuint imageBase, uint singletonRva, uint offset, int size)
    {
        try
        {
            if (imageBase == 0 || (ulong)imageBase + singletonRva + 4 > uint.MaxValue) return null;
            Span<byte> pointer = stackalloc byte[4];
            if (!memory.TryRead(imageBase + singletonRva, pointer)) return null;
            var target = BinaryPrimitives.ReadUInt32LittleEndian(pointer);
            if (target == 0 || (ulong)target + offset + (uint)size > uint.MaxValue) return null;
            Span<byte> bytes = stackalloc byte[size];
            if (!memory.TryRead(target + offset, bytes)) return null;
            return size == 1 ? bytes[0] : BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        }
        catch (Exception) { return null; }
    }

    public static RenderTextureAllocationEstimate Estimate(int width, int height, RenderTextureState state)
    {
        if (state.ContentScaleFactor is not float scale || !float.IsFinite(scale) || scale <= 0 ||
            state.SupportsNpot is not bool npot || width <= 0 || height <= 0)
            return new(null, null, null, null, "Allocation estimate needs positive dimensions, a valid scale and NPOT proof.");
        // Match the audited CVTDQ2PS/MULSS/CVTTSS2SI sequence, including float rounding.
        var scaledWidth = (float)width * scale;
        var scaledHeight = (float)height * scale;
        if (!float.IsFinite(scaledWidth) || !float.IsFinite(scaledHeight) || scaledWidth < 1 || scaledHeight < 1 ||
            scaledWidth >= 2147483648f || scaledHeight >= 2147483648f)
            return new(null, null, null, null, "Scaled dimensions are outside the positive native integer range.");
        var pixelWidth = (uint)(int)scaledWidth;
        var pixelHeight = (uint)(int)scaledHeight;
        if (!npot)
        {
            pixelWidth = BitOperations.RoundUpToPowerOf2(pixelWidth);
            pixelHeight = BitOperations.RoundUpToPowerOf2(pixelHeight);
        }
        // malloc receives the low 32 bits of IMUL/SHL. Keep a separate wide estimate so
        // an oversized request cannot masquerade as a small legitimate allocation.
        var nativeBytes = unchecked(pixelWidth * pixelHeight * 4u);
        try
        {
            var bytes = checked((ulong)pixelWidth * pixelHeight * 4);
            return new(pixelWidth, pixelHeight, bytes, nativeBytes,
                bytes > uint.MaxValue ? "Native 32-bit allocation byte count wraps." : null);
        }
        catch (OverflowException)
        {
            return new(pixelWidth, pixelHeight, null, nativeBytes, "Allocation estimate exceeds 64 bits; native byte count wraps.");
        }
    }
}

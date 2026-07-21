using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace ChronoTriggerAccessibility.Prism;

internal static class PrismLibraryResolver
{
    internal const string ExpectedSha256 = "6A84322E42D1B4123E2E66E9887CFDF0CDEA2A972FA40FC7B7185AEC77F5178A";
    private static int installed;

    internal static void Install(Assembly assembly)
    {
        if (Interlocked.Exchange(ref installed, 1) == 0)
        {
            NativeLibrary.SetDllImportResolver(assembly, Resolve);
        }
    }

    internal static string GetValidatedAssemblySiblingPath(string assemblyDirectory)
    {
        var path = Path.Combine(assemblyDirectory, "prism.dll");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The verified Prism library was not found beside the assembly.", path);
        }

        using var stream = File.OpenRead(path);
        var actualHash = Convert.ToHexString(SHA256.HashData(stream));
        if (!StringComparer.Ordinal.Equals(ExpectedSha256, actualHash))
        {
            throw new FileLoadException("The assembly-sibling Prism library does not match the reviewed SHA-256.", path);
        }

        return path;
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, "prism", StringComparison.Ordinal))
        {
            return IntPtr.Zero;
        }

        var assemblyDirectory = Path.GetDirectoryName(assembly.Location)
            ?? throw new FileLoadException("Could not determine the Prism assembly directory.");
        return NativeLibrary.Load(GetValidatedAssemblySiblingPath(assemblyDirectory));
    }
}

using System.Security.Cryptography;

namespace ChronoTriggerAccessibility.Mod.AudioDescriptions;

public static class StoryActionAssets
{
    public const string SupportedResourceHash = "09914BF4A8944C0708C947E7EB5683AEE4ED48BC6D5D1D75FA07B68131653C7E";

    public static async Task<bool> VerifyAsync(string gameRoot, Action<string> diagnostic)
    {
        try
        {
            await using var source = new FileStream(Path.Combine(gameRoot, "resources.bin"), FileMode.Open,
                FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(source));
            var supported = hash == SupportedResourceHash;
            diagnostic(supported
                ? "Story action graphics and scripts: supported resource hash verified."
                : "Story action descriptions unavailable: resources.bin differs from the reviewed graphics and scripts.");
            return supported;
        }
        catch (Exception exception)
        {
            try { diagnostic($"Story action resource verification failed: {exception.GetType().Name}: {exception.Message}"); }
            catch (Exception) { }
            return false;
        }
    }
}

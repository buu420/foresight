using System.Security.Cryptography;

namespace ChronoTriggerAccessibility.Mod.AudioDescriptions;

/// <summary>The decoded tmp.mp4 of an unmodified Steam movie. VideoPlayer::setFileName
/// (libcocos2d RVA 0x287FAA) XOR-decodes the DAT before Media Foundation opens it.</summary>
public sealed record OriginalMovie(string FileName, string DecodedSha256, long DecodedBytes, double DurationSeconds);

public static class MovieNarrationCatalog
{
    // The opening (001.dat) is absent: its descriptions already use the Fish timeline.
    public static IReadOnlyList<OriginalMovie> Originals { get; } =
    [
        new("002.dat", "BA85684DC8586665C0A78B0AA7E8B704622ECA0A4993076AD319DFE84FDB1254", 23838176, 39.105708),
        new("003.dat", "8F0FB77315D033D9B4C48831D4BE89CD64AA2A935D540E967810CA8BE32A58AF", 41682024, 65.498708),
        new("004.dat", "64734088FA2480B7559FF18C109D8D6A9B71662707A3A0AA3643B1F8F6854451", 33284746, 52.018604),
        new("006.dat", "95F627AFE3DA97D9924CD96349329F1C0F488ADEF1D2BAB4D5F36A0481782509", 15679299, 25.0583),
        new("007.dat", "6860FC51185DBCC2009ECBE57154CC0E10879F811EE1F99DF9908051AF399704", 33765590, 54.0873),
        new("007-en.dat", "C84685BC35F79F25E20C2FA637DF20ACB0BFABD22648314A83F5409434B00B52", 33868434, 54.0873),
    ];

    public static OriginalMovie? Match(string decodedMovie, IEnumerable<OriginalMovie> candidates)
    {
        try
        {
            // Size first, so unrelated movies are not hashed on the game thread.
            var length = new FileInfo(decodedMovie).Length;
            var sized = candidates.Where(movie => movie.DecodedBytes == length).ToArray();
            if (sized.Length == 0) return null;
            using var input = File.OpenRead(decodedMovie);
            var actual = Convert.ToHexString(SHA256.HashData(input));
            var matches = sized.Where(movie => movie.DecodedSha256.Equals(actual, StringComparison.OrdinalIgnoreCase)).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

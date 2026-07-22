using System.Collections.Concurrent;

namespace ChronoTriggerAccessibility.Native.Capture;

public sealed class LocalizedTextCache
{
    private readonly ConcurrentDictionary<(int FileId, int MessageId), string> entries = new();

    public void Store(int fileId, int messageId, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        entries[(fileId, messageId)] = new string(value.AsSpan());
    }

    public bool TryGet(int fileId, int messageId, out string value) =>
        entries.TryGetValue((fileId, messageId), out value!);
}

namespace APIRelay;

internal static class ProtocolTraceCatalog
{
    internal static IReadOnlyList<ProtocolTraceGroup> GroupEntries(IReadOnlyList<ProtocolTraceEntry> entries)
    {
        var groups = new List<ProtocolTraceGroup>();
        var groupsById = new Dictionary<uint, ProtocolTraceGroup>();

        foreach (var entry in entries)
        {
            if (!groupsById.TryGetValue(entry.RequestId, out var group))
            {
                group = new ProtocolTraceGroup(entry.RequestId);
                groupsById.Add(entry.RequestId, group);
                groups.Add(group);
            }

            group.Entries.Add(entry);
        }

        return groups;
    }
    internal static IReadOnlyList<string> EnumerateLogFiles(string directory, int maxFileCount)
    {
        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        return Enumerable.Range(1, maxFileCount)
            .Select(index => Path.Combine(directory, index == 1 ? "protocol-trace.txt" : $"protocol-trace-{index}.txt"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();
    }

    internal sealed class ProtocolTraceGroup(uint requestId)
    {
        public uint RequestId { get; } = requestId;
        public List<ProtocolTraceEntry> Entries { get; } = [];
    }
}

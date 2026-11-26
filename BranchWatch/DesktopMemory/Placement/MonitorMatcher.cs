namespace BranchWatch;

internal static class MonitorMatcher
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    internal static IReadOnlyList<MonitorMatch> Match(IReadOnlyList<SnapshotMonitor> saved,
        IReadOnlyList<SnapshotMonitor> current)
    {
        var savedIds = saved.Select(ActiveIdentities).ToArray();
        var currentIds = current.Select(ActiveIdentities).ToArray();
        var result = new List<MonitorMatch>(saved.Count);
        for (var index = 0; index < saved.Count; index++)
        {
            var source = saved[index].SourceName;
            var ids = savedIds[index];
            if (ids.Length == 0 || (ids.Length == 1 && ids[0].Length == 0))
            {
                result.Add(new(source, null, MonitorMatchKind.IdentityUnavailable));
                continue;
            }

            if (ids.Length != 1 || savedIds.Count(list => list.Contains(ids[0], Comparer)) != 1)
            {
                result.Add(new(source, null, MonitorMatchKind.Ambiguous));
                continue;
            }

            var matches = Enumerable.Range(0, current.Count)
                .Where(i => currentIds[i].Contains(ids[0], Comparer)).ToArray();
            if (matches.Length == 0)
            {
                result.Add(new(source, null, currentIds.Any(list => list.Length == 0 || list.Contains("", Comparer))
                    ? MonitorMatchKind.IdentityUnavailable : MonitorMatchKind.Missing));
            }
            else if (matches.Length != 1 || currentIds[matches[0]].Length != 1)
            {
                result.Add(new(source, null, MonitorMatchKind.Ambiguous));
            }
            else
            {
                result.Add(new(source, current[matches[0]].SourceName, MonitorMatchKind.PhysicalIdentity));
            }
        }

        return result;
    }

    private static string[] ActiveIdentities(SnapshotMonitor monitor) =>
        monitor.Devices.Where(d => (d.Flags & 1) != 0)
            .Select(d => string.IsNullOrWhiteSpace(d.InterfacePath) ? "" : d.InterfacePath!)
            .ToArray();
}

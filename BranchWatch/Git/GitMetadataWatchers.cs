using System.IO;

namespace BranchWatch;

internal sealed class GitMetadataWatchers : IDisposable
{
    [Flags]
    internal enum Activity
    {
        None = 0,
        Refresh = 1,
        Index = 2
    }

    private readonly Action _scheduleRefresh;
    private readonly Action? _scheduleIndexActivity;
    private readonly Dictionary<string, DirectorySubscription> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public GitMetadataWatchers(Action scheduleRefresh, Action? scheduleIndexActivity = null)
    {
        _scheduleRefresh = scheduleRefresh;
        _scheduleIndexActivity = scheduleIndexActivity;
    }

    internal int WatcherCount => _subscriptions.Count;
    internal int CreatedWatcherCount { get; private set; }

    public void Reconcile(RepositoryInfo repository, RepositoryStatus status)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var desired = new Dictionary<string, Dictionary<string, Activity>>(StringComparer.OrdinalIgnoreCase);

        void Add(string directory, string name, Activity activity)
        {
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(name)) return;
            if (!desired.TryGetValue(directory, out var names))
            {
                names = new(StringComparer.OrdinalIgnoreCase);
                desired.Add(directory, names);
            }

            names[name] = names.GetValueOrDefault(name) | activity;
        }

        Add(repository.GitDirectory, "HEAD", Activity.Refresh);
        Add(repository.CommonDirectory, "packed-refs", Activity.Refresh);
        if (_scheduleIndexActivity is not null)
            Add(repository.GitDirectory, "index", Activity.Index);

        if (!string.IsNullOrWhiteSpace(status.RefPath))
            Add(Path.GetDirectoryName(status.RefPath) ?? "", Path.GetFileName(status.RefPath), Activity.Refresh);

        foreach (var (directory, subscription) in _subscriptions.ToArray())
        {
            if (desired.TryGetValue(directory, out var names) && subscription.Matches(names))
                continue;

            _subscriptions.Remove(directory);
            subscription.Dispose();
        }

        foreach (var (directory, names) in desired)
        {
            if (_subscriptions.ContainsKey(directory)) continue;
            var subscription = DirectorySubscription.TryCreate(directory, names, Dispatch);
            if (subscription is null) continue;
            _subscriptions.Add(directory, subscription);
            CreatedWatcherCount++;
        }
    }

    internal static Activity Classify(IReadOnlyDictionary<string, Activity> names, string? name, string? oldName = null)
    {
        var activity = name is not null && names.TryGetValue(name, out var current) ? current : Activity.None;
        if (oldName is not null && names.TryGetValue(oldName, out var previous)) activity |= previous;
        return activity;
    }

    private void Dispatch(Activity activity)
    {
        if ((activity & Activity.Refresh) != 0) _scheduleRefresh();
        if ((activity & Activity.Index) != 0) _scheduleIndexActivity?.Invoke();
    }

    public void Clear()
    {
        foreach (var subscription in _subscriptions.Values) subscription.Dispose();
        _subscriptions.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Clear();
    }

    private sealed class DirectorySubscription : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly Dictionary<string, Activity> _names;
        private readonly Action<Activity> _dispatch;
        private volatile bool _active = true;
        private volatile bool _failed;

        private DirectorySubscription(FileSystemWatcher watcher, Dictionary<string, Activity> names, Action<Activity> dispatch)
        {
            _watcher = watcher;
            _names = names;
            _dispatch = dispatch;
        }

        internal static DirectorySubscription? TryCreate(string directory, Dictionary<string, Activity> names, Action<Activity> dispatch)
        {
            FileSystemWatcher? watcher = null;
            DirectorySubscription? subscription = null;
            try
            {
                if (!Directory.Exists(directory)) return null;
                watcher = new FileSystemWatcher(directory)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                    IncludeSubdirectories = false
                };
                foreach (var name in names.Keys) watcher.Filters.Add(name);
                subscription = new DirectorySubscription(watcher, names, dispatch);
                watcher.Changed += subscription.OnChange;
                watcher.Created += subscription.OnChange;
                watcher.Deleted += subscription.OnChange;
                watcher.Renamed += subscription.OnRename;
                watcher.Error += subscription.OnError;
                watcher.EnableRaisingEvents = true;
                return subscription;
            }
            catch
            {
                if (subscription is not null) subscription.Dispose();
                else watcher?.Dispose();
                return null;
            }
        }

        internal bool Matches(Dictionary<string, Activity> names) =>
            !_failed && _active && _names.Count == names.Count
            && _names.All(pair => names.TryGetValue(pair.Key, out var activity) && pair.Value == activity);

        private void OnChange(object sender, FileSystemEventArgs e)
        {
            if (_active && !_failed) _dispatch(Classify(_names, e.Name));
        }

        private void OnRename(object sender, RenamedEventArgs e)
        {
            if (_active && !_failed) _dispatch(Classify(_names, e.Name, e.OldName));
        }

        private void OnError(object sender, ErrorEventArgs e)
        {
            if (!_active || _failed) return;
            _failed = true;
            _dispatch(Activity.Refresh);
        }

        public void Dispose()
        {
            _active = false;
            _watcher.Dispose();
        }
    }
}

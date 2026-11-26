using System.IO;
using Activity = BranchWatch.GitMetadataWatchers.Activity;

namespace BranchWatch.Tests;

[TestClass]
public sealed class GitMetadataWatchersTests
{
    [TestMethod]
    public void Reconcile_SharesDirectoryAndRetainsUnchangedSubscriptions()
    {
        StaTest.WithTemporaryDirectory(directory =>
        {
            var git = Path.Combine(directory, ".git");
            var refs = Path.Combine(git, "refs", "heads");
            Directory.CreateDirectory(refs);
            var repository = new RepositoryInfo(directory, git, git);
            var status = RepositoryStatus.Empty with { RefPath = Path.Combine(refs, "main") };
            using var watchers = new GitMetadataWatchers(() => { }, () => { });
            watchers.Reconcile(repository, status);
            Assert.AreEqual(2, watchers.WatcherCount);
            Assert.AreEqual(2, watchers.CreatedWatcherCount);

            for (var i = 0; i < 20; i++) watchers.Reconcile(repository, status);
            Assert.AreEqual(2, watchers.CreatedWatcherCount);

            watchers.Reconcile(repository, status with { RefPath = Path.Combine(refs, "feature") });
            Assert.AreEqual(2, watchers.WatcherCount);
            Assert.AreEqual(3, watchers.CreatedWatcherCount);

            watchers.Reconcile(repository, status with { RefPath = null });
            Assert.AreEqual(1, watchers.WatcherCount);
            Assert.AreEqual(3, watchers.CreatedWatcherCount);

            watchers.Clear();
            Assert.AreEqual(0, watchers.WatcherCount);
            watchers.Reconcile(repository, status);
            Assert.AreEqual(2, watchers.WatcherCount);
            Assert.AreEqual(5, watchers.CreatedWatcherCount);
            watchers.Dispose();
            Assert.AreEqual(0, watchers.WatcherCount);
            Assert.ThrowsExactly<ObjectDisposedException>(() => watchers.Reconcile(repository, status));
        });
    }

    [TestMethod]
    public void Reconcile_LinkedWorktreeKeepsPrivateAndCommonMetadataSeparate()
    {
        StaTest.WithTemporaryDirectory(directory =>
        {
            var git = Path.Combine(directory, "worktree");
            var common = Path.Combine(directory, "common");
            var refs = Path.Combine(common, "refs", "heads");
            Directory.CreateDirectory(git);
            Directory.CreateDirectory(refs);
            var repository = new RepositoryInfo(directory, git, common);
            var status = RepositoryStatus.Empty with { RefPath = Path.Combine(refs, "main") };
            using var watchers = new GitMetadataWatchers(() => { }, () => { });
            watchers.Reconcile(repository, status);
            Assert.AreEqual(3, watchers.WatcherCount);
            watchers.Reconcile(repository, status);
            Assert.AreEqual(3, watchers.CreatedWatcherCount);
            watchers.Reconcile(new RepositoryInfo(directory, common, common), status);
            Assert.AreEqual(2, watchers.WatcherCount);
            Assert.AreEqual(4, watchers.CreatedWatcherCount);
        });
    }

    [TestMethod]
    public void Reconcile_MissingDirectoryCanBeRecoveredOnSubsequentRefresh()
    {
        StaTest.WithTemporaryDirectory(directory =>
        {
            var git = Path.Combine(directory, ".git");
            Directory.CreateDirectory(directory);
            var repository = new RepositoryInfo(directory, git, git);
            using var watchers = new GitMetadataWatchers(() => { });
            watchers.Reconcile(repository, RepositoryStatus.Empty);
            Assert.AreEqual(0, watchers.WatcherCount);
            Directory.CreateDirectory(git);
            watchers.Reconcile(repository, RepositoryStatus.Empty);
            Assert.AreEqual(1, watchers.WatcherCount);
            Assert.AreEqual(1, watchers.CreatedWatcherCount);
        });
    }

    [TestMethod]
    public void Classify_RenamesRouteBothNamesWithoutPromotingUnrelatedFiles()
    {
        var names = new Dictionary<string, Activity>(StringComparer.OrdinalIgnoreCase)
        {
            ["HEAD"] = Activity.Refresh,
            ["packed-refs"] = Activity.Refresh,
            ["index"] = Activity.Index
        };
        Assert.AreEqual(Activity.Refresh, GitMetadataWatchers.Classify(names, "HEAD"));
        Assert.AreEqual(Activity.Index, GitMetadataWatchers.Classify(names, "INDEX"));
        Assert.AreEqual(Activity.None, GitMetadataWatchers.Classify(names, "config"));
        Assert.AreEqual(Activity.None, GitMetadataWatchers.Classify(names, "index.lock"));
        Assert.AreEqual(Activity.Index, GitMetadataWatchers.Classify(names, "index", "index.lock"));
        Assert.AreEqual(Activity.Refresh, GitMetadataWatchers.Classify(names, "old-head", "HEAD"));
        Assert.AreEqual(Activity.Refresh | Activity.Index, GitMetadataWatchers.Classify(names, "index", "HEAD"));
        Assert.AreEqual(Activity.Refresh, GitMetadataWatchers.Classify(names, "packed-refs", "HEAD"));
    }

    [TestMethod]
    public async Task SharedWatcher_DetectsHeadIndexAndNewPackedRefs()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var refresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var index = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var watchers = new GitMetadataWatchers(() => refresh.TrySetResult(), () => index.TrySetResult());
            var repository = new RepositoryInfo(directory, directory, directory);
            watchers.Reconcile(repository, RepositoryStatus.Empty);
            Assert.AreEqual(1, watchers.WatcherCount);

            File.WriteAllText(Path.Combine(directory, "packed-refs"), "");
            await refresh.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsFalse(index.Task.IsCompleted);

            File.WriteAllText(Path.Combine(directory, "index.lock"), "index");
            File.Move(Path.Combine(directory, "index.lock"), Path.Combine(directory, "index"));
            await index.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(1, watchers.CreatedWatcherCount);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task PinnedWatcher_DetectsHeadRenameWithoutIndexSubscription()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var watchers = new GitMetadataWatchers(() => refreshed.TrySetResult());
            var repository = new RepositoryInfo(directory, directory, directory);
            watchers.Reconcile(repository, RepositoryStatus.Empty);
            File.WriteAllText(Path.Combine(directory, "HEAD.lock"), "ref: refs/heads/main");
            File.Move(Path.Combine(directory, "HEAD.lock"), Path.Combine(directory, "HEAD"));
            await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(1, watchers.WatcherCount);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}

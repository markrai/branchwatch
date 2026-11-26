using System.Text.Json;

namespace BranchWatch;

internal sealed record DesktopRestoreRequest(DesktopSnapshot Snapshot, DesktopPlacementPlan Reviewed,
    UnmatchedMonitorPolicy Policy, IReadOnlySet<Guid> Selected, bool LaunchMissing);

internal sealed record DesktopRestorePreparation(ExistingRestorePreparation Existing, MissingApplicationLaunchPreparation? Launches)
{
    internal ExistingRestoreItem[] Ready => Existing.Items.Where(i => i.Status == ExistingRestoreStatus.Ready).ToArray();
    internal MissingApplicationLaunchItem[] LaunchReady =>
        Launches?.Items.Where(i => i.Status == ExistingRestoreStatus.LaunchReady).ToArray() ?? [];
}

internal sealed class DesktopRestoreSession : IDisposable
{
    private enum Stage { Created, Preparing, Prepared, Executing, Finished, Disposed }
    private readonly DesktopRestoreRequest _request;
    private readonly Func<IReadOnlyList<DesktopSnapshot>> _load;
    private readonly ExistingWindowRestoreService _existing;
    private readonly MissingApplicationLaunchService _launches;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly CancellationToken _token;
    private readonly object _progressGate = new();
    private readonly Dictionary<Guid, ExistingRestoreOutcome> _progress = [];
    private DesktopRestorePreparation? _prepared;
    private int _stage;
    private int _executionStarted;

    internal DesktopRestoreSession(DesktopRestoreRequest request, Func<IReadOnlyList<DesktopSnapshot>> load,
        IExistingWindowRestoreEnvironment environment, IMissingApplicationLaunchEnvironment launcher)
    {
        _request = request with { Selected = request.Selected.ToHashSet() };
        _load = load;
        _existing = new(environment);
        _launches = new(environment, launcher);
        _token = _cancellation.Token;
    }

    internal static DesktopRestoreSession CreateNative(DesktopRestoreRequest request, DesktopMemoryStore store) =>
        new(request, store.Load, new NativeExistingWindowRestoreEnvironment(), new NativeMissingApplicationLaunchEnvironment());

    internal bool IsCancellationRequested => _token.IsCancellationRequested;
    internal bool IsExecuting => (Stage)Volatile.Read(ref _stage) == Stage.Executing;
    internal bool ExecutionStarted => Volatile.Read(ref _executionStarted) != 0;

    internal void Cancel()
    {
        if ((Stage)Volatile.Read(ref _stage) != Stage.Disposed) _cancellation.Cancel();
    }

    internal void ThrowIfCancelled() => _token.ThrowIfCancellationRequested();

    internal DesktopRestorePreparation Prepare()
    {
        Transition(Stage.Created, Stage.Preparing);
        try
        {
            ThrowIfCancelled();
            if (_request.Selected.Count == 0) throw new InvalidOperationException("Select at least one eligible window.");
            VerifySavedSnapshot();
            var existing = RestoreSelection.Apply(_existing.Prepare(_request.Snapshot, _request.Reviewed,
                _request.Policy, _token), _request.Selected);
            ThrowIfCancelled();
            var launches = _request.LaunchMissing ? _launches.Prepare(existing, _token) : null;
            ThrowIfCancelled();
            var prepared = new DesktopRestorePreparation(existing, launches);
            _prepared = prepared;
            Transition(Stage.Preparing, Stage.Prepared);
            return prepared;
        }
        catch
        {
            Transition(Stage.Preparing, Stage.Finished);
            throw;
        }
    }

    internal IReadOnlyList<ExistingRestoreOutcome> ExecuteApproved(DesktopRestorePreparation approval,
        Action<ExistingRestoreOutcome>? observer = null)
    {
        if (!ReferenceEquals(_prepared, approval)) throw new InvalidOperationException("Restore approval does not belong to this session.");
        Transition(Stage.Prepared, Stage.Executing);
        try
        {
            ThrowIfCancelled();
            VerifySavedSnapshot();
            ThrowIfCancelled();
            Volatile.Write(ref _executionStarted, 1);
            var pendingLaunches = approval.Launches?.Items.Select(i => i.RestoreItem.WindowId).ToHashSet() ?? [];
            void Report(ExistingRestoreOutcome outcome)
            {
                lock (_progressGate) _progress[outcome.WindowId] = outcome;
                RestoreProgressReporter.Notify(observer, outcome);
            }

            var results = _existing.Execute(approval.Existing, _token, outcome =>
            {
                if (!pendingLaunches.Contains(outcome.WindowId)) Report(outcome);
            });
            return approval.Launches is null ? results : _launches.Execute(approval.Launches, results, _token, Report);
        }
        finally
        {
            Transition(Stage.Executing, Stage.Finished);
        }
    }

    internal IReadOnlyDictionary<Guid, ExistingRestoreOutcome> ProgressSnapshot()
    {
        lock (_progressGate) return new Dictionary<Guid, ExistingRestoreOutcome>(_progress);
    }

    private void VerifySavedSnapshot()
    {
        var saved = _request.Snapshot;
        var current = _load().SingleOrDefault(s => s.DesktopId == saved.DesktopId);
        if (current is null || JsonSerializer.Serialize(current) != JsonSerializer.Serialize(saved))
            throw new InvalidOperationException("The saved snapshot changed. Refresh before restoring.");
    }

    private void Transition(Stage expected, Stage next)
    {
        if (Interlocked.CompareExchange(ref _stage, (int)next, (int)expected) != (int)expected)
            throw new InvalidOperationException("Restore session is no longer in the expected state.");
    }

    public void Dispose()
    {
        var stage = (Stage)Volatile.Read(ref _stage);
        if (stage == Stage.Disposed) return;
        if (stage is Stage.Preparing or Stage.Executing)
            throw new InvalidOperationException("Cancel and await the active restore operation before disposal.");
        Transition(stage, Stage.Disposed);
        _cancellation.Dispose();
    }
}

namespace BranchWatch;

internal static class RestoreProgressReporter
{
    internal static void Notify(Action<ExistingRestoreOutcome>? observer, ExistingRestoreOutcome outcome)
    {
        try { observer?.Invoke(outcome); }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning("Desktop Memory progress observer failed: {0}", ex.GetType().Name);
        }
    }
}

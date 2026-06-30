namespace BranchWatch;

internal static class WorkspaceActivityReasonFormatter
{
    public static string? Format(WorkspaceActivityReason? reason)
    {
        return reason switch
        {
            WorkspaceActivityReason.WorkspaceLoaded => "workspace loaded",
            WorkspaceActivityReason.WorkspaceRescanned => "workspace rescanned",
            WorkspaceActivityReason.BranchChanged => "branch changed",
            WorkspaceActivityReason.IndexChanged => "index changed",
            WorkspaceActivityReason.FileChanged => "file changed",
            WorkspaceActivityReason.RepoOpened => "repo opened",
            _ => null
        };
    }

    public static string FormatOrNone(WorkspaceActivityReason? reason) => Format(reason) ?? "(none)";

    public static string? FormatForOverlay(AppSettings settings, WorkspaceActivityReason? reason)
    {
        if (!settings.ShowWorkspaceActivityReason || settings.WatchMode != RepositoryWatchMode.WorkspaceRepo)
        {
            return null;
        }

        return Format(reason);
    }
}

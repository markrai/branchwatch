using System.IO;

namespace BranchWatch;

internal sealed record TrayNoteChoice(Guid Id, string Label);

internal sealed record TrayMenuState(string Mode, string Repository, string ActiveRepository, string RepositoryTooltip,
    string Branch, string BranchTooltip, string Activity, bool ShowActivity, string? Status, string? StatusTooltip,
    bool CanRescan, bool CanRefresh, bool WorkspaceMode, bool PinnedMode, string OverlayToggle, string DesktopOverlayToggle,
    string NotifyText)
{
    internal static TrayMenuState From(AppSettings settings, RepositoryStatus status, WorkspaceActivityReason? reason, string? workspaceStatus)
    {
        var workspace = settings.WatchMode == RepositoryWatchMode.WorkspaceRepo;
        var root = status.RepositoryRoot;
        var branch = status.BranchDisplay;
        var message = !string.IsNullOrWhiteSpace(status.ErrorMessage) ? status.ErrorMessage : workspaceStatus;
        if (string.IsNullOrWhiteSpace(message)) message = null;
        return new(
            workspace ? "Mode: Workspace Mode" : "Mode: Pinned Repo Mode",
            workspace ? $"Workspace: {RepositoryLabel(settings.WorkspaceRootPath)}" : $"Pinned repo: {RepositoryLabel(root)}",
            $"Repository: {CompactRepositoryLabel(root)}", root ?? "",
            $"Branch: {Compact(branch, 45)}", branch,
            $"Last activity: {WorkspaceActivityReasonFormatter.FormatOrNone(reason)}", workspace && reason.HasValue,
            message is null ? null : $"Status: {Compact(message, 48)}", message,
            workspace && !string.IsNullOrWhiteSpace(settings.WorkspaceRootPath), !string.IsNullOrWhiteSpace(root),
            workspace, settings.WatchMode == RepositoryWatchMode.PinnedRepo,
            settings.OverlayVisible ? "Hide overlay" : "Show overlay",
            settings.VirtualDesktopOverlayVisible ? "Hide overlay" : "Show overlay",
            Compact(string.IsNullOrWhiteSpace(root) ? "BranchWatch" : $"BranchWatch - {branch}", 63));
    }

    internal static TrayNoteChoice[] Notes(IEnumerable<StickyNote> notes, Func<Guid, string> desktopName) =>
        notes.Select(note => new TrayNoteChoice(note.Id, $"{note.CustomTitle ?? note.Preview} · {desktopName(note.DesktopId)}"
            + (note.IsAlarmRinging ? " (alarm due)" : note.AlarmDueUtc.HasValue ? " (alarm set)" : "")
            + (note.IsOpen ? "" : " (closed)"))).ToArray();

    internal static string Literal(string value) => value.Replace("&", "&&", StringComparison.Ordinal);

    private static string Compact(string value, int limit) => value.Length > limit ? value[..(limit - 3)] + "..." : value;

    private static string CompactRepositoryLabel(string? root)
    {
        if (string.IsNullOrWhiteSpace(root)) return "(none)";
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(root));
        return string.IsNullOrWhiteSpace(name) ? root : name;
    }

    private static string RepositoryLabel(string? root) => string.IsNullOrWhiteSpace(root) ? "(none)" : root;
}

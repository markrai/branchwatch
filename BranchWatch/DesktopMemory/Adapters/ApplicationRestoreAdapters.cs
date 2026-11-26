using System.IO;

namespace BranchWatch;

internal enum ApplicationFolderKind
{
    Cursor,
    VisualStudioCode,
    Explorer,
    WindowsTerminal
}

internal sealed record ApplicationFolderContext(ApplicationFolderKind Kind, string Folder);

internal sealed record ApplicationLaunchSpec(string ExecutablePath, ApplicationFolderContext? Context,
    IReadOnlyList<string> Arguments, bool OpenOnly)
{
    internal string Description => Context is null ? ExecutablePath
        : $"{ExecutablePath} · {Context.Kind} · {Context.Folder}{(OpenOnly ? " (open only; position not verified)" : "")}";
}

internal static class ApplicationRestoreAdapters
{
    internal static ApplicationFolderKind? Detect(SnapshotWindow window)
    {
        if (string.IsNullOrWhiteSpace(window.ExecutablePath) || string.IsNullOrWhiteSpace(window.ClassName))
            return null;

        var name = Path.GetFileName(window.ExecutablePath) ?? string.Empty;
        if (window.ClassName == "Chrome_WidgetWin_1")
        {
            if (name.Equals("Cursor.exe", StringComparison.OrdinalIgnoreCase)) return ApplicationFolderKind.Cursor;
            if (name.Equals("Code.exe", StringComparison.OrdinalIgnoreCase)) return ApplicationFolderKind.VisualStudioCode;
        }
        if (name.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)
            && window.ClassName is "CabinetWClass" or "ExploreWClass")
            return ApplicationFolderKind.Explorer;
        if (name.Equals("WindowsTerminal.exe", StringComparison.OrdinalIgnoreCase)
            && window.ClassName == "CASCADIA_HOSTING_WINDOW_CLASS")
            return ApplicationFolderKind.WindowsTerminal;
        return null;
    }

    internal static bool IsValidContext(SnapshotWindow window, ApplicationFolderContext context) =>
        Detect(window) == context.Kind && IsCanonicalFolder(context.Folder)
        && (context.Kind != ApplicationFolderKind.WindowsTerminal || !context.Folder.Contains(';'))
        && (context.Kind != ApplicationFolderKind.Explorer || !context.Folder.Contains(','));

    internal static bool IsCanonicalFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length < 3 || path.Length > 32760
            || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\'
            || path.AsSpan(2).Contains(':') || path.Contains('"') || path.Any(char.IsControl))
            return false;
        try
        {
            return Path.IsPathFullyQualified(path)
                && string.Equals(Path.GetFullPath(path), path, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { return false; }
    }

    internal static ApplicationLaunchSpec Build(SnapshotWindow window)
    {
        var path = window.ExecutablePath ?? throw new InvalidOperationException("Saved executable path is missing.");
        if (window.FolderContext is not { } context) return new(path, null, [], false);
        if (!IsValidContext(window, context)) throw new InvalidOperationException("Invalid application folder context.");
        return context.Kind switch
        {
            ApplicationFolderKind.Cursor => new(path, context, ["--new-window", context.Folder], false),
            ApplicationFolderKind.VisualStudioCode => new(path, context, ["--new-window", context.Folder], false),
            ApplicationFolderKind.Explorer => new(path, context, [context.Folder], true),
            ApplicationFolderKind.WindowsTerminal => new(path, context, ["-w", "new", "new-tab", "-d", context.Folder], true),
            _ => throw new InvalidOperationException("Unsupported folder adapter.")
        };
    }

    internal static string? CheckFolder(ApplicationLaunchSpec spec)
    {
        if (spec.Context is not { } context) return null;
        try
        {
            if (!IsCanonicalFolder(context.Folder)
                || new DriveInfo(Path.GetPathRoot(context.Folder)!).DriveType != DriveType.Fixed
                || !Directory.Exists(context.Folder))
                return "Saved folder is unavailable or not on a fixed local drive.";
            for (DirectoryInfo? directory = new DirectoryInfo(context.Folder); directory is not null; directory = directory.Parent)
            {
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    return "Saved folder is a reparse point or beneath one.";
            }
            return null;
        }
        catch (Exception ex) { return $"Saved folder could not be verified: {ex.GetType().Name}."; }
    }
}

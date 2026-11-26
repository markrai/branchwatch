using System.Diagnostics;
using System.IO;

namespace BranchWatch;

internal sealed class NativeMissingApplicationLaunchEnvironment : IMissingApplicationLaunchEnvironment
{
    private static readonly object Gate = new();
    private static readonly HashSet<string> Attempted = new(StringComparer.OrdinalIgnoreCase);

    public string? CheckLaunchBlock(string path)
    {
        lock (Gate)
        {
            if (Attempted.Contains(path))
                return "This executable has already been attempted during this BranchWatch session. Check the running application before trying again after restarting BranchWatch.";
        }
        try
        {
            if (!Path.IsPathFullyQualified(path) || path.Length < 4 || !char.IsAsciiLetter(path[0])
                || path[1] != ':' || path[2] != '\\' || !Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFullPath(path), path, StringComparison.OrdinalIgnoreCase)
                || path.AsSpan(2).Contains(':'))
                return "Only canonical, local absolute executable paths are supported.";
            if (new DriveInfo(Path.GetPathRoot(path)!).DriveType != DriveType.Fixed || !File.Exists(path))
                return "Executable is unavailable or not on a fixed local drive.";
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                return "Executable is a directory, alias or reparse point.";
            for (DirectoryInfo? directory = new DirectoryInfo(Path.GetDirectoryName(path)!); directory is not null; directory = directory.Parent)
            {
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    return "Executable is beneath a reparse-point directory.";
            }
            var name = Path.GetFileNameWithoutExtension(path);
            if (string.Equals(name, "BranchWatch", StringComparison.OrdinalIgnoreCase))
                return "BranchWatch itself is excluded.";
            var processes = Process.GetProcessesByName(name);
            try
            {
                if (processes.Length > 0)
                    return "A process with this executable name is already running. Generic launch is skipped to avoid duplicate or single-instance activation.";
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }
            return null;
        }
        catch (Exception ex)
        {
            return $"Executable or process inventory could not be verified: {ex.GetType().Name}.";
        }
    }

    public LaunchedProcessIdentity Launch(string path, Guid desktopId, CurrentDisplayTopology topology,
        CancellationToken cancellation)
    {
        lock (Gate)
        {
            cancellation.ThrowIfCancellationRequested();
            if (VirtualDesktopRegistryReader.TryGetVerifiedDisplayState()?.ActiveDesktop.Id != desktopId
                || !ExistingWindowRestoreService.SameDisplays(topology, DesktopMemoryProbe.CaptureDisplays()))
                throw new InvalidOperationException("Desktop or monitor configuration changed before launch.");
            if (CheckLaunchBlock(path) is { } reason)
                throw new InvalidOperationException(reason);
            cancellation.ThrowIfCancellationRequested();
            if (VirtualDesktopRegistryReader.TryGetVerifiedDisplayState()?.ActiveDesktop.Id != desktopId)
                throw new InvalidOperationException("Active desktop changed before launch.");
            Attempted.Add(path);
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                WorkingDirectory = Path.GetDirectoryName(path)!,
                UseShellExecute = false
            }) ?? throw new InvalidOperationException("Windows did not return a process.");
            if (!ProbeNative.GetProcessTimes(process.Handle, out var creation, out _, out _, out _) || creation <= 0)
                throw new InvalidOperationException("The started process identity could not be verified.");
            return new((uint)process.Id, creation);
        }
    }

    public bool IsRunning(LaunchedProcessIdentity identity)
    {
        try
        {
            using var process = Process.GetProcessById(checked((int)identity.ProcessId));
            if (process.HasExited) return false;
            var handle = ProbeNative.OpenProcess(0x1000, false, identity.ProcessId);
            if (handle == IntPtr.Zero) return false;
            try
            {
                return ProbeNative.GetProcessTimes(handle, out var creation, out _, out _, out _)
                    && creation == identity.CreatedFileTime && !process.HasExited;
            }
            finally { ProbeNative.CloseHandle(handle); }
        }
        catch (Exception) { return false; }
    }

    public bool WaitForInventory(CancellationToken cancellation) =>
        !cancellation.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(250));
}

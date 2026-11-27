using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BranchWatch;

internal sealed class VirtualDesktopSessionKeyPath
{
    private readonly Func<string> _resolvePath;
    private string? _path;
    private object? _gate;

    internal VirtualDesktopSessionKeyPath()
        : this(TryGetNativeSessionId, GetManagedSessionId)
    {
    }

    internal VirtualDesktopSessionKeyPath(Func<uint?> nativeSessionId, Func<uint> managedSessionId)
    {
        _resolvePath = () => FormattableString.Invariant(
            $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\{nativeSessionId() ?? managedSessionId()}\VirtualDesktops");
    }

    internal string Value => LazyInitializer.EnsureInitialized(ref _path, ref _gate, _resolvePath);

    private static uint? TryGetNativeSessionId()
    {
        return ProcessIdToSessionId((uint)Environment.ProcessId, out var sessionId) ? sessionId : null;
    }

    private static uint GetManagedSessionId()
    {
        using var process = Process.GetCurrentProcess();
        return checked((uint)process.SessionId);
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);
}

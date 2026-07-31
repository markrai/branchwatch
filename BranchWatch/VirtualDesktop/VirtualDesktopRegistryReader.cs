using System.Diagnostics;
using Microsoft.Win32;

namespace BranchWatch;

public static class VirtualDesktopRegistryReader
{
    internal const string VirtualDesktopsKeyPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";

    private const int GuidByteLength = 16;

    public static VirtualDesktopInfo? TryGetCurrentDesktop()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(VirtualDesktopsKeyPath, writable: false);
            if (key is null)
            {
                return null;
            }

            var desktopIds = ParseDesktopIds(key.GetValue("VirtualDesktopIDs") as byte[]);
            if (desktopIds.Count == 0)
            {
                return null;
            }

            var primaryCurrent = ParseGuidValue(key.GetValue("CurrentVirtualDesktop"));
            var sessionCurrent = TryGetSessionCurrentDesktopId();
            var currentId = SelectCurrentDesktopId(primaryCurrent, sessionCurrent, desktopIds);
            if (currentId is null)
            {
                return null;
            }

            var index = desktopIds.ToList().IndexOf(currentId.Value);
            if (index < 0)
            {
                index = 0;
            }

            var name = TryGetDesktopName(key, currentId.Value);
            return new VirtualDesktopInfo(currentId.Value, ResolveDisplayName(index, name));
        }
        catch
        {
            return null;
        }
    }

    public static IReadOnlyList<Guid> ParseDesktopIds(byte[]? virtualDesktopIds)
    {
        if (virtualDesktopIds is null || virtualDesktopIds.Length < GuidByteLength)
        {
            return Array.Empty<Guid>();
        }

        var ids = new List<Guid>();
        var span = virtualDesktopIds.AsSpan();
        while (span.Length >= GuidByteLength)
        {
            ids.Add(new Guid(span[..GuidByteLength]));
            span = span[GuidByteLength..];
        }

        return ids;
    }

    public static Guid? ParseGuidValue(object? value)
    {
        return value switch
        {
            byte[] bytes when bytes.Length >= GuidByteLength => new Guid(bytes.AsSpan(0, GuidByteLength)),
            string text when Guid.TryParse(text, out var parsed) => parsed,
            _ => null
        };
    }

    public static Guid? SelectCurrentDesktopId(
        Guid? primaryCurrent,
        Guid? sessionCurrent,
        IReadOnlyList<Guid> allDesktopIds)
    {
        if (primaryCurrent.HasValue && allDesktopIds.Contains(primaryCurrent.Value))
        {
            return primaryCurrent;
        }

        if (sessionCurrent.HasValue && allDesktopIds.Contains(sessionCurrent.Value))
        {
            return sessionCurrent;
        }

        return allDesktopIds.Count > 0 ? allDesktopIds[0] : null;
    }

    public static string ResolveDisplayName(int zeroBasedIndex, string? registryName)
    {
        if (!string.IsNullOrWhiteSpace(registryName))
        {
            return registryName.Trim();
        }

        return $"Desktop {zeroBasedIndex + 1}";
    }

    private static Guid? TryGetSessionCurrentDesktopId()
    {
        var sessionId = Process.GetCurrentProcess().SessionId;
        var sessionKeyPath = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\{sessionId}\VirtualDesktops";

        using var sessionKey = Registry.CurrentUser.OpenSubKey(sessionKeyPath, writable: false);
        return sessionKey is null ? null : ParseGuidValue(sessionKey.GetValue("CurrentVirtualDesktop"));
    }

    private static string? TryGetDesktopName(RegistryKey virtualDesktopsKey, Guid desktopId)
    {
        using var desktopKey = virtualDesktopsKey.OpenSubKey($@"Desktops\{desktopId:B}", writable: false);
        return desktopKey?.GetValue("Name") as string;
    }
}

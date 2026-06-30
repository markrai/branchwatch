using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace BranchWatch;

internal static class BranchWatchApplicationIdentity
{
    public static string UserHash { get; } = CreateUserHash();

    public static string MutexName => $@"Local\BranchWatch-{UserHash}";

    public static string PipeName => $"BranchWatch-{UserHash}";

    public static bool IsPrimaryInstanceRunning()
    {
        if (!Mutex.TryOpenExisting(MutexName, out var mutex))
        {
            return false;
        }

        mutex.Dispose();
        return true;
    }

    private static string CreateUserHash()
    {
        var user = WindowsIdentity.GetCurrent().User?.Value
            ?? Environment.UserName
            ?? "unknown";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user)))[..16];
    }
}

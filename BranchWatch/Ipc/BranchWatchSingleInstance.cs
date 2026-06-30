using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace BranchWatch;

internal sealed class BranchWatchSingleInstance : IDisposable
{
    private readonly Mutex _mutex;

    private BranchWatchSingleInstance(Mutex mutex)
    {
        _mutex = mutex;
    }

    public static BranchWatchSingleInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, CreateCurrentUserMutexName(), out var createdNew);
        if (createdNew)
        {
            return new BranchWatchSingleInstance(mutex);
        }

        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        try
        {
            _mutex.ReleaseMutex();
        }
        catch
        {
        }

        _mutex.Dispose();
    }

    private static string CreateCurrentUserMutexName()
    {
        var user = WindowsIdentity.GetCurrent().User?.Value
            ?? Environment.UserName
            ?? "unknown";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user)))[..16];
        return $@"Local\BranchWatch-{hash}";
    }
}

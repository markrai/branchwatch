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
        var mutex = new Mutex(initiallyOwned: true, BranchWatchApplicationIdentity.MutexName, out var createdNew);
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
}

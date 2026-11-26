using System.IO;
using System.Runtime.ExceptionServices;

namespace BranchWatch.Tests;

internal static class StaTest
{
    internal static void Run(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    internal static void WithTemporaryDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try { Run(() => action(directory)); }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}

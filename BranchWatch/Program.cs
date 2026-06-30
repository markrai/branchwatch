using System.Windows;

namespace BranchWatch;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (ActivityCommand.TryExecute(args, out var exitCode))
        {
            return exitCode;
        }

        SingleInstanceBootstrap.AcquiredInstance = BranchWatchSingleInstance.TryAcquire();
        if (SingleInstanceBootstrap.AcquiredInstance is null)
        {
            return 0;
        }

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}

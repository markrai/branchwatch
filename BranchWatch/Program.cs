using System.Windows;

namespace BranchWatch;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (DesktopMemoryProbeCommand.TryExecute(args, out var probeExitCode))
        {
            return probeExitCode;
        }

        if (ActivityCommand.TryExecute(args, out var exitCode))
        {
            return exitCode;
        }

        SingleInstanceBootstrap.AcquiredInstance = BranchWatchSingleInstance.TryAcquire();
        if (SingleInstanceBootstrap.AcquiredInstance is null)
        {
            return 0;
        }

        ApplyRenderingExperiment(
            Environment.GetEnvironmentVariable("BRANCHWATCH_EXPERIMENT_SOFTWARE_RENDERING"),
            () => System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly);

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    internal static void ApplyRenderingExperiment(string? value, Action enableSoftwareRendering)
    {
        if (value == "1")
        {
            enableSoftwareRendering();
        }
    }
}

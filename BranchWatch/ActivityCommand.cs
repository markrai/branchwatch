using System.Runtime.InteropServices;

namespace BranchWatch;

internal static class ActivityCommand
{
    private const int AttachParentProcess = -1;

    public static bool TryExecute(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0 || !string.Equals(args[0], "activity", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        EnsureConsole();

        if (!TryParse(args, out var path, out var reason, out var errorMessage))
        {
            Console.Error.WriteLine(errorMessage);
            exitCode = 1;
            return true;
        }

        if (!BranchWatchApplicationIdentity.IsPrimaryInstanceRunning())
        {
            Console.Error.WriteLine("BranchWatch is not running.");
            exitCode = 1;
            return true;
        }

        var response = BranchWatchIpcClient.TrySendActivity(path, reason);
        if (response is null)
        {
            Console.Error.WriteLine("Unable to connect to BranchWatch.");
            exitCode = 1;
            return true;
        }

        if (response.Promoted)
        {
            Console.WriteLine(response.Message);
        }
        else
        {
            Console.WriteLine(response.Message);
        }

        exitCode = response.Success ? 0 : 1;
        return true;
    }

    private static bool TryParse(string[] args, out string path, out string reason, out string errorMessage)
    {
        path = string.Empty;
        reason = string.Empty;
        errorMessage = string.Empty;

        if (args.Length < 2)
        {
            errorMessage = "Usage: BranchWatch.exe activity \"<path>\" --reason repo-opened";
            return false;
        }

        path = args[1];

        string? parsedReason = null;
        for (var index = 2; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--reason", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    errorMessage = "Missing value for --reason.";
                    return false;
                }

                parsedReason = args[index + 1];
                index++;
                continue;
            }

            errorMessage = $"Unknown argument: {args[index]}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(parsedReason))
        {
            errorMessage = "Missing required argument: --reason repo-opened";
            return false;
        }

        if (!string.Equals(parsedReason, "repo-opened", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "Unsupported activity reason. Supported value: repo-opened";
            return false;
        }

        reason = parsedReason;
        return true;
    }

    private static void EnsureConsole()
    {
        if (!AttachConsole(AttachParentProcess))
        {
            AllocConsole();
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();
}

using System.Text.Json;

namespace BranchWatch;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (!AllowedOrigin(args)) return 2;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var request = BrowserCompanionProtocol.ParseHello(await BrowserCompanionProtocol.ReadAsync(
                Console.OpenStandardInput(), deadline.Token));
            CompanionReply reply;
            try { reply = await BrowserCompanionProtocol.ConnectAsync(BrowserCompanionProtocol.PipeName, request, deadline.Token); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException
                or JsonException or InvalidOperationException or FormatException)
            {
                reply = new(1, "unavailable", request.ProfileId, request.Nonce);
            }
            await BrowserCompanionProtocol.WriteAsync(Console.OpenStandardOutput(), BrowserCompanionProtocol.Encode(reply), deadline.Token);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Browser companion host failed: {ex.GetType().Name}");
            return 1;
        }
    }

    private static bool AllowedOrigin(string[] args)
    {
        if (args.Length != 2 || !args[1].StartsWith("--parent-window=", StringComparison.Ordinal)
            || !ulong.TryParse(args[1].AsSpan(16), out _)) return false;
        var origin = args[0];
        const string prefix = "chrome-extension://";
        if (!origin.StartsWith(prefix, StringComparison.Ordinal) || !origin.EndsWith('/') || origin.Length != prefix.Length + 33
            || !origin.AsSpan(prefix.Length, 32).ToArray().All(c => c is >= 'a' and <= 'p')) return false;
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Host path unavailable.");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(executable)!, "host.json")));
        var root = manifest.RootElement;
        return root.GetProperty("name").GetString() == "com.branchwatch.companion"
            && string.Equals(root.GetProperty("path").GetString(), executable, StringComparison.OrdinalIgnoreCase)
            && root.GetProperty("allowed_origins").EnumerateArray().Any(v => v.GetString() == origin);
    }
}

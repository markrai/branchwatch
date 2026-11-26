using System.Text.Json;

namespace BranchWatch;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (!AllowedOrigin(args)) return 2;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var bytes = BrowserCompanionProtocol.ReadAsync(Console.OpenStandardInput(), deadline.Token, BrowserTabCapture.MaxFrame).GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("command", out var command)
                && command.GetString() == "capture")
            {
                Capture(args, BrowserTabCapture.ParseRequest(bytes), deadline.Token);
                return 0;
            }
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("command", out var restoreCommand)
                && restoreCommand.GetString() == "restore")
            {
                Restore(args, BrowserRestoreProtocol.ParseRequest(bytes), deadline.Token);
                return 0;
            }
            var request = BrowserCompanionProtocol.ParseHello(bytes);
            CompanionReply reply;
            try { reply = BrowserCompanionProtocol.ConnectAsync(BrowserCompanionProtocol.PipeName, request, deadline.Token).GetAwaiter().GetResult(); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or OperationCanceledException
                or JsonException or InvalidOperationException or FormatException)
            {
                reply = new(1, "unavailable", request.ProfileId, request.Nonce);
            }
            BrowserCompanionProtocol.WriteAsync(Console.OpenStandardOutput(), BrowserCompanionProtocol.Encode(reply), deadline.Token).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Browser companion host failed: {ex.GetType().Name}");
            return 1;
        }
    }

    private static void Capture(string[] args, BrowserCaptureRequest request, CancellationToken cancellation)
    {
        var status = "rejected";
        if (BrowserProbeProtocol.TryParent(args, out var parent))
        {
            var observation = NativeObservation.Inspect(new(request.WindowId, request.Nonce, default), parent);
            if (observation.Status == "observed-not-authorized" && observation.Browser is { } browser
                && observation.Handle is { } handle && observation.ProcessId is { } pid
                && observation.ProcessCreation is { } creation && observation.Membership?.DesktopId is { } desktop)
            {
                var envelope = new BrowserCaptureEnvelope(request, browser, new(handle, pid, creation), desktop);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                timeout.CancelAfter(BrowserCompanionProtocol.Timeout);
                using var pipe = new System.IO.Pipes.NamedPipeClientStream(".", BrowserCompanionProtocol.PipeName,
                    System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly,
                    System.Security.Principal.TokenImpersonationLevel.Anonymous);
                pipe.ConnectAsync(timeout.Token).GetAwaiter().GetResult();
                BrowserCompanionProtocol.WriteAsync(pipe, BrowserCompanionProtocol.Encode(envelope), timeout.Token,
                    BrowserTabCapture.MaxFrame).GetAwaiter().GetResult();
                var bytes = BrowserCompanionProtocol.ReadAsync(pipe, timeout.Token).GetAwaiter().GetResult();
                var reply = JsonSerializer.Deserialize<CompanionReply>(bytes, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (reply is { Version: 1, Status: "received" } && reply.ProfileId == request.ProfileId && reply.Nonce == request.Nonce)
                    status = "received";
            }
        }
        BrowserCompanionProtocol.WriteAsync(Console.OpenStandardOutput(),
            BrowserCompanionProtocol.Encode(new CompanionReply(1, status, request.ProfileId, request.Nonce)), cancellation).GetAwaiter().GetResult();
    }

    private static void Restore(string[] args, BrowserRestoreRequest request, CancellationToken cancellation)
    {
        BrowserRestoreReply Rejected() => new(1, "rejected", request.ProfileId, request.Nonce);
        var reply = Rejected();
        if (BrowserProbeProtocol.TryParent(args, out var parent))
        {
            var probeRequest = new BrowserProbeRequest(request.WindowId, request.Nonce, default);
            var observation = NativeObservation.Inspect(probeRequest, parent);
            if (observation.Status == "observed-not-authorized" && observation.Browser is { } browser
                && observation.Membership?.DesktopId is { } desktop)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                timeout.CancelAfter(BrowserCompanionProtocol.Timeout);
                using var pipe = new System.IO.Pipes.NamedPipeClientStream(".", BrowserCompanionProtocol.PipeName,
                    System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly,
                    System.Security.Principal.TokenImpersonationLevel.Anonymous);
                pipe.ConnectAsync(timeout.Token).GetAwaiter().GetResult();
                var envelope = new BrowserRestoreEnvelope(request, browser, desktop);
                BrowserCompanionProtocol.WriteAsync(pipe, BrowserCompanionProtocol.Encode(envelope), timeout.Token).GetAwaiter().GetResult();
                var bytes = BrowserCompanionProtocol.ReadAsync(pipe, timeout.Token, BrowserTabCapture.MaxFrame).GetAwaiter().GetResult();
                reply = BrowserRestoreProtocol.ParseReply(bytes, request);
                var after = NativeObservation.Inspect(probeRequest, parent);
                if (after != observation) reply = Rejected();
            }
        }
        BrowserCompanionProtocol.WriteAsync(Console.OpenStandardOutput(),
            BrowserCompanionProtocol.Encode(reply), cancellation, BrowserTabCapture.MaxFrame).GetAwaiter().GetResult();
    }

    private static bool AllowedOrigin(string[] args)
    {
        if (args.Length != 2 || !args[1].StartsWith("--parent-window=", StringComparison.Ordinal)
            || !ulong.TryParse(args[1].AsSpan(16), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _)) return false;
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

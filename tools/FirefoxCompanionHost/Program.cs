using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace BranchWatch;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args is ["--self-test"])
            {
                SelfTest();
                Console.Error.WriteLine("Firefox Companion protocol self-tests passed.");
                return 0;
            }

            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Host path unavailable.");
            var manifest = Path.Combine(Path.GetDirectoryName(executable)!, "host.json");
            if (!FirefoxProtocol.AllowedInvocation(args, executable, manifest, File.ReadAllText(manifest))) return 2;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var bytes = BrowserCompanionProtocol.ReadAsync(Console.OpenStandardInput(), deadline.Token).GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(bytes);
            var command = doc.RootElement.GetProperty("command").GetString();
            byte[] reply;
            if (command == "hello")
            {
                var request = BrowserCompanionProtocol.ParseHello(bytes);
                CompanionReply response;
                try
                {
                    response = BrowserCompanionProtocol.ConnectAsync(BrowserCompanionProtocol.PipeName, request, deadline.Token)
                        .GetAwaiter().GetResult();
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
                    or OperationCanceledException or JsonException or InvalidOperationException or FormatException)
                {
                    response = new(1, "unavailable", request.ProfileId, request.Nonce);
                }
                reply = BrowserCompanionProtocol.Encode(response);
            }
            else if (command == "observe")
            {
                var request = FirefoxProtocol.ParseObservation(bytes);
                var foreground = GetForegroundWindow();
                var observation = NativeObservation.Inspect(request, foreground, firefoxDiagnostic: true);
                if (GetForegroundWindow() != foreground)
                    observation = new(1, request.WindowId, request.Nonce, "foreground-changed");
                reply = BrowserCompanionProtocol.Encode(observation);
            }
            else if (command is "challenge-present" or "challenge-absent")
            {
                var request = FirefoxProtocol.ParseChallenge(bytes, command);
                reply = BrowserCompanionProtocol.Encode(FirefoxChallengeObservation.Inspect(request, command == "challenge-present"));
            }
            else return 2;

            BrowserCompanionProtocol.WriteAsync(Console.OpenStandardOutput(), reply, deadline.Token,
                BrowserProbeProtocol.MaximumRequestBytes).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Firefox Companion diagnostic failed: {ex.GetType().Name}");
            return 1;
        }
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Firefox diagnostic self-test failed.");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or InvalidOperationException or OverflowException or FormatException) { return; }
        throw new InvalidOperationException("Malformed diagnostic accepted.");
    }

    private static void SelfTest()
    {
        var exe = Path.GetFullPath("FirefoxCompanionHost.exe");
        var manifest = Path.GetFullPath("host.json");
        var json = JsonSerializer.Serialize(new
        {
            name = FirefoxProtocol.HostName, description = "Probe", path = exe, type = "stdio",
            allowed_extensions = new[] { FirefoxProtocol.ExtensionId }
        });
        Require(FirefoxProtocol.AllowedInvocation([manifest, FirefoxProtocol.ExtensionId], exe, manifest, json));
        Require(!FirefoxProtocol.AllowedInvocation([manifest, "wrong@example.org"], exe, manifest, json));
        Require(!FirefoxProtocol.AllowedInvocation([Path.GetFullPath("other.json"), FirefoxProtocol.ExtensionId], exe, manifest, json));
        Require(!FirefoxProtocol.AllowedInvocation([manifest], exe, manifest, json));
        Require(!FirefoxProtocol.AllowedInvocation([manifest, FirefoxProtocol.ExtensionId], exe, manifest,
            json.Replace(FirefoxProtocol.ExtensionId, "other@example.org")));
        const string observation = """{"version":1,"command":"observe","windowId":7,"nonce":"0123456789abcdef0123456789abcdef","left":-500,"top":0,"width":400,"height":300}""";
        var request = FirefoxProtocol.ParseObservation(Encoding.UTF8.GetBytes(observation));
        var present = observation.Replace("\"observe\"", "\"challenge-present\"");
        var absent = observation.Replace("\"observe\"", "\"challenge-absent\"");
        Require(FirefoxProtocol.ParseChallenge(Encoding.UTF8.GetBytes(present), "challenge-present") == request);
        Require(FirefoxProtocol.ParseChallenge(Encoding.UTF8.GetBytes(absent), "challenge-absent") == request);
        Reject(() => FirefoxProtocol.ParseChallenge(Encoding.UTF8.GetBytes(present), "challenge-absent"));
        Reject(() => FirefoxProtocol.ParseChallenge(Encoding.UTF8.GetBytes(present), "restore"));
        Reject(() => FirefoxProtocol.ParseChallenge(Encoding.UTF8.GetBytes(present.Replace("\"version\":1", "\"version\":1,\"url\":\"https://example.com\"")), "challenge-present"));
        var marker = FirefoxChallengeObservation.Marker(request.Nonce);
        Require(FirefoxChallengeObservation.HasMarker(marker + "Firefox", request.Nonce));
        Require(!FirefoxChallengeObservation.HasMarker("other " + marker, request.Nonce));
        Require(!FirefoxChallengeObservation.HasMarker(marker.TrimEnd() + "x", request.Nonce));
        Require(request.WindowId == 7 && request.Bounds == new ProbeRect(-500, 0, -100, 300));
        foreach (var invalid in new[]
        {
            observation.Replace("\"observe\"", "\"capture\""),
            observation.Replace("\"observe\"", "\"restore\""),
            observation.Replace("\"version\":1", "\"version\":1,\"version\":1"),
            observation.Replace("\"version\":1", "\"version\":1,\"url\":\"https://example.com\""),
            observation.Replace("-500", "2147483647"),
            observation.Replace("\"width\":400", "\"width\":0")
        }) Reject(() => FirefoxProtocol.ParseObservation(Encoding.UTF8.GetBytes(invalid)));
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}

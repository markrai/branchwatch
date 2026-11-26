using System.Buffers.Binary;
using System.Text;

namespace BranchWatch;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--self-test"])
        {
            SelfTest();
            Console.Error.WriteLine("Browser probe protocol self-tests passed.");
            return 0;
        }

        try
        {
            if (!BrowserProbeProtocol.TryParent(args, out var parent)) return 2;
            var request = BrowserProbeProtocol.Read(Console.OpenStandardInput());
            var response = NativeObservation.Inspect(request, parent);
            BrowserProbeProtocol.Write(Console.OpenStandardOutput(), response);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Browser correlation probe failed: {ex.GetType().Name}");
            return 1;
        }
    }

    private static MemoryStream Frame(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var stream = new MemoryStream();
        Span<byte> prefix = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(prefix, (uint)bytes.Length);
        stream.Write(prefix);
        stream.Write(bytes);
        stream.Position = 0;
        return stream;
    }

    private static void Require(bool value)
    {
        if (!value) throw new InvalidOperationException("Browser probe self-test assertion failed.");
    }

    private static void Reject(Stream stream)
    {
        try { BrowserProbeProtocol.Read(stream); }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or System.Text.Json.JsonException or OverflowException or InvalidOperationException or FormatException) { return; }
        throw new InvalidOperationException("Malformed request was accepted.");
    }

    private static void SelfTest()
    {
        const string origin = "chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/";
        Require(BrowserProbeProtocol.TryParent([origin, "--parent-window=42"], out var parent) && parent == new IntPtr(42));
        Require(!BrowserProbeProtocol.TryParent([origin, "--parent-window=0"], out _));
        Require(!BrowserProbeProtocol.TryParent(["https://example.com/", "--parent-window=42"], out _));
        Require(!BrowserProbeProtocol.TryParent([origin, "--parent-window=42", "extra"], out _));
        const string json = """{"version":1,"windowId":7,"nonce":"0123456789abcdef0123456789abcdef","left":-500,"top":0,"width":400,"height":300}""";
        using var valid = Frame(json);
        var request = BrowserProbeProtocol.Read(valid);
        Require(request.WindowId == 7 && request.Bounds == new ProbeRect(-500, 0, -100, 300));
        using var duplicate = Frame(json.Replace("\"version\":1", "\"version\":1,\"version\":1"));
        Reject(duplicate);
        using var url = Frame(json.Replace("\"version\":1", "\"version\":1,\"url\":\"https://example.com\""));
        Reject(url);
        using var version = Frame(json.Replace("\"version\":1", "\"version\":2"));
        Reject(version);
        using var overflow = Frame(json.Replace("-500", "2147483647"));
        Reject(overflow);
        using var oversized = Frame(new string('x', 4097));
        Reject(oversized);
        using var truncated = new MemoryStream([1, 0, 0, 0]);
        Reject(truncated);
        Require(NativeObservation.Compare(request.Bounds, request.Bounds, null, 96) == "exact-window-rect");
        Require(NativeObservation.Compare(request.Bounds, request.Bounds, null, 144) == "scaled-coordinate-space-unqualified");
        Require(NativeObservation.Compare(request.Bounds, new ProbeRect(-508, 0, -92, 308), request.Bounds, 96) == "exact-visible-frame");
        Require(NativeObservation.Compare(request.Bounds, new ProbeRect(0, 0, 500, 400), null, 96) == "bounds-differ");
        using var response = new MemoryStream();
        BrowserProbeProtocol.Write(response, new(1, 7, request.Nonce, "observed-not-authorized"));
        var data = response.ToArray();
        Require(BinaryPrimitives.ReadUInt32LittleEndian(data) == data.Length - 4);
    }
}

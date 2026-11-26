using System.Buffers.Binary;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BranchWatch;

internal sealed record BrowserProbeRequest(int WindowId, string Nonce, ProbeRect Bounds);
internal sealed record BrowserProbeResponse(int Version, int WindowId, string Nonce, string Status,
    string? Browser = null, string? Handle = null, uint? ProcessId = null, uint? ThreadId = null,
    string? ProcessCreation = null, ProbeDesktopMembership? Membership = null, uint? Dpi = null,
    ProbeRect? ScreenBounds = null, ProbeRect? FrameBounds = null, string? Geometry = null);

internal static partial class BrowserProbeProtocol
{
    internal const int MaximumRequestBytes = 4096;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [GeneratedRegex(@"\Achrome-extension://[a-p]{32}/?\z", RegexOptions.CultureInvariant)]
    private static partial Regex OriginPattern();

    [GeneratedRegex(@"\A[0-9a-f]{32}\z", RegexOptions.CultureInvariant)]
    private static partial Regex NoncePattern();

    internal static bool TryParent(string[] args, out IntPtr parent)
    {
        parent = IntPtr.Zero;
        if (args.Length != 2 || !OriginPattern().IsMatch(args[0])
            || !args[1].StartsWith("--parent-window=", StringComparison.Ordinal)
            || !long.TryParse(args[1].AsSpan(16), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var value)
            || value <= 0 || (IntPtr.Size == 4 && value > uint.MaxValue)) return false;
        parent = IntPtr.Size == 4 ? new IntPtr(unchecked((int)value)) : new IntPtr(value);
        return true;
    }

    internal static BrowserProbeRequest Read(Stream input)
    {
        Span<byte> prefix = stackalloc byte[4];
        input.ReadExactly(prefix);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(prefix);
        if (length == 0 || length > MaximumRequestBytes) throw new InvalidDataException("Invalid message length.");
        var bytes = new byte[(int)length];
        input.ReadExactly(bytes);
        using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Expected object.");
        var names = root.EnumerateObject().Select(p => p.Name).ToArray();
        string[] allowed = ["version", "windowId", "nonce", "left", "top", "width", "height"];
        if (names.Length != allowed.Length || names.Distinct(StringComparer.Ordinal).Count() != allowed.Length
            || names.Any(n => !allowed.Contains(n, StringComparer.Ordinal)))
            throw new InvalidDataException("Unexpected message properties.");
        if (root.GetProperty("version").GetInt32() != 1) throw new InvalidDataException("Unsupported version.");
        var id = root.GetProperty("windowId").GetInt32();
        var nonce = root.GetProperty("nonce").GetString();
        var left = root.GetProperty("left").GetInt32();
        var top = root.GetProperty("top").GetInt32();
        var width = root.GetProperty("width").GetInt32();
        var height = root.GetProperty("height").GetInt32();
        if (id < 0 || nonce is null || !NoncePattern().IsMatch(nonce) || width <= 0 || height <= 0)
            throw new InvalidDataException("Invalid window observation.");
        return new(id, nonce, new(left, top, checked(left + width), checked(top + height)));
    }

    internal static void Write(Stream output, BrowserProbeResponse response)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(response, Options);
        Span<byte> prefix = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(prefix, (uint)bytes.Length);
        output.Write(prefix);
        output.Write(bytes);
        output.Flush();
    }
}

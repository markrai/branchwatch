using System.IO;
using System.Text.Json;

namespace BranchWatch;

internal sealed record BrowserSavedTab(string Url, bool Pinned, bool Active);
internal sealed record BrowserWindowContext(string Browser, string ProfileId, IReadOnlyList<BrowserSavedTab> Tabs);
internal sealed record BrowserCaptureRequest(int Version, string Command, string ProfileId, string Nonce,
    string Ticket, int WindowId, IReadOnlyList<BrowserSavedTab> Tabs, int Omitted);
internal sealed record BrowserNativeToken(string Handle, uint ProcessId, string ProcessCreation);
internal sealed record BrowserCaptureEnvelope(BrowserCaptureRequest Request, string Browser, BrowserNativeToken Token, Guid DesktopId);
internal sealed record BrowserCaptureTicket(string Code, Guid DesktopId, Guid WindowId, string Browser, BrowserNativeToken Token, long Started);

internal static class BrowserTabCapture
{
    internal const int MaxTabs = 100;
    internal const int MaxUrlLength = 2048;
    internal const int MaxFrame = 256 * 1024;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };

    internal static string? BrowserFor(string? path, string? className) =>
        className != "Chrome_WidgetWin_1" || string.IsNullOrWhiteSpace(path) ? null
        : Path.GetFileName(path)?.ToLowerInvariant() switch
        {
            "chrome.exe" => "chrome",
            "msedge.exe" => "edge",
            _ => null
        };

    internal static bool IsId(string? id) => id is { Length: 32 }
        && id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static bool IsUrl(string? value) => value is { Length: > 0 and <= MaxUrlLength }
        && !value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && !string.IsNullOrEmpty(uri.Host)
        && string.IsNullOrEmpty(uri.UserInfo);

    internal static bool IsValid(BrowserWindowContext? context) => context is not null
        && context.Browser is "chrome" or "edge" && IsId(context.ProfileId)
        && context.Tabs is { Count: > 0 and <= MaxTabs }
        && context.Tabs.All(t => t is not null && IsUrl(t.Url))
        && context.Tabs.Count(t => t.Active) <= 1;

    internal static BrowserCaptureRequest ParseRequest(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        return ParseRequest(doc.RootElement);
    }

    private static BrowserCaptureRequest ParseRequest(JsonElement root)
    {
        ExactFields(root, ["version","command","profileId","nonce","ticket","windowId","tabs","omitted"]);
        foreach (var tab in root.GetProperty("tabs").EnumerateArray()) ExactFields(tab, ["url","pinned","active"]);
        var request = root.Deserialize<BrowserCaptureRequest>(Options) ?? throw new InvalidDataException("Invalid capture request.");
        if (request.Version != 1 || request.Command != "capture" || !IsId(request.Nonce)
            || !IsId(request.Ticket) || request.WindowId < 0 || request.Omitted < 0
            || !IsValid(new("chrome", request.ProfileId, request.Tabs)))
            throw new InvalidDataException("Invalid tab capture.");
        return request;
    }

    internal static BrowserCaptureEnvelope ParseEnvelope(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        var root = doc.RootElement;
        ExactFields(root, ["request","browser","token","desktopId"]);
        ExactFields(root.GetProperty("token"), ["handle","processId","processCreation"]);
        var request = ParseRequest(root.GetProperty("request"));
        var envelope = root.Deserialize<BrowserCaptureEnvelope>(Options) ?? throw new InvalidDataException("Invalid capture envelope.");
        if (envelope.Browser is not ("chrome" or "edge") || envelope.Token is not { } token
            || token.ProcessId == 0 || envelope.DesktopId == Guid.Empty || token.Handle is null
            || !token.Handle.StartsWith("0x", StringComparison.Ordinal)
            || !long.TryParse(token.Handle.AsSpan(2), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var handle) || handle <= 0
            || !long.TryParse(token.ProcessCreation, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var creation) || creation <= 0)
            throw new InvalidDataException("Invalid native observation.");
        return envelope with { Request = request };
    }

    private static void ExactFields(JsonElement element, string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Expected object.");
        var actual = element.EnumerateObject().Select(p => p.Name).ToArray();
        if (actual.Length != names.Length || actual.Distinct(StringComparer.Ordinal).Count() != names.Length
            || actual.Any(n => !names.Contains(n, StringComparer.Ordinal)))
            throw new InvalidDataException("Unexpected capture properties.");
    }
}

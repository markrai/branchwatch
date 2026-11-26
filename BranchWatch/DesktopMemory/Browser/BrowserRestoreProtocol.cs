using System.IO;
using System.Text.Json;

namespace BranchWatch;

internal sealed record BrowserRestoreRequest(int Version, string Command, string ProfileId, string Nonce, string Ticket, int WindowId);
internal sealed record BrowserRestoreEnvelope(BrowserRestoreRequest RestoreRequest, string Browser, Guid DesktopId);
internal sealed record BrowserRestoreReply(int Version, string Status, string ProfileId, string Nonce, IReadOnlyList<BrowserSavedTab>? Tabs = null);

internal static class BrowserRestoreProtocol
{
    internal const int Version = 2;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };

    internal static BrowserRestoreRequest ParseRequest(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes);
        return ParseRequest(doc.RootElement);
    }

    private static BrowserRestoreRequest ParseRequest(JsonElement root)
    {
        Exact(root, ["version", "command", "profileId", "nonce", "ticket", "windowId"]);
        var request = root.Deserialize<BrowserRestoreRequest>(Options) ?? throw new InvalidDataException("Invalid restore request.");
        if (request.Version != Version || request.Command != "restore" || !BrowserTabCapture.IsId(request.ProfileId)
            || !BrowserTabCapture.IsId(request.Nonce) || !BrowserTabCapture.IsId(request.Ticket) || request.WindowId < 0)
            throw new InvalidDataException("Invalid restore request.");
        return request;
    }

    internal static BrowserRestoreEnvelope ParseEnvelope(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;
        Exact(root, ["restoreRequest", "browser", "desktopId"]);
        var request = ParseRequest(root.GetProperty("restoreRequest"));
        var envelope = root.Deserialize<BrowserRestoreEnvelope>(Options) ?? throw new InvalidDataException("Invalid restore envelope.");
        if (envelope.Browser is not ("chrome" or "edge") || envelope.DesktopId == Guid.Empty)
            throw new InvalidDataException("Invalid restore observation.");
        return envelope with { RestoreRequest = request };
    }

    internal static BrowserRestoreReply ParseReply(byte[] bytes, BrowserRestoreRequest request)
    {
        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;
        Exact(root, ["version", "status", "profileId", "nonce", "tabs"]);
        var reply = root.Deserialize<BrowserRestoreReply>(Options) ?? throw new InvalidDataException("Invalid restore reply.");
        if (reply.Version != Version || reply.ProfileId != request.ProfileId || reply.Nonce != request.Nonce
            || reply.Status is not ("authorized" or "rejected"))
            throw new InvalidDataException("Restore response did not match.");
        if (reply.Status == "rejected")
        {
            if (reply.Tabs is not null) throw new InvalidDataException("Rejected response contained URLs.");
        }
        else
        {
            if (root.GetProperty("tabs").ValueKind != JsonValueKind.Array) throw new InvalidDataException("Expected authorized tab list.");
            foreach (var tab in root.GetProperty("tabs").EnumerateArray()) Exact(tab, ["url", "pinned", "active"]);
            if (!CanRestore(new("chrome", reply.ProfileId, reply.Tabs!))) throw new InvalidDataException("Invalid authorized tabs.");
        }
        return reply;
    }

    internal static bool CanRestore(BrowserWindowContext? context)
    {
        if (context is null || !BrowserTabCapture.IsValid(context) || context.Tabs.Count == 0) return false;
        var unpinned = false;
        foreach (var tab in context.Tabs)
        {
            if (!tab.Pinned) unpinned = true;
            else if (unpinned) return false;
        }
        return BrowserCompanionProtocol.Encode(new BrowserRestoreReply(Version, "authorized", context.ProfileId,
            new string('0', 32), context.Tabs)).Length <= BrowserTabCapture.MaxFrame;
    }

    private static void Exact(JsonElement root, string[] fields)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Expected object.");
        var names = root.EnumerateObject().Select(p => p.Name).ToArray();
        if (names.Length != fields.Length || names.Distinct(StringComparer.Ordinal).Count() != fields.Length
            || names.Any(n => !fields.Contains(n, StringComparer.Ordinal)))
            throw new InvalidDataException("Unexpected restore fields.");
    }
}

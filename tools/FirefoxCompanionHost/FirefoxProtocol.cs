using System.Text.Json;

namespace BranchWatch;

internal static class FirefoxProtocol
{
    internal const string HostName = "com.branchwatch.firefox_probe";
    internal const string ExtensionId = "branchwatch-companion@branchwatch.local";

    internal static bool AllowedInvocation(string[] args, string executable, string manifestPath, string manifestJson)
    {
        if (args.Length != 2 || args[1] != ExtensionId
            || !Path.IsPathFullyQualified(args[0])
            || !string.Equals(Path.GetFullPath(args[0]), Path.GetFullPath(manifestPath), StringComparison.OrdinalIgnoreCase))
            return false;

        using var doc = JsonDocument.Parse(manifestJson, new JsonDocumentOptions { MaxDepth = 4 });
        var root = doc.RootElement;
        Exact(root, ["name", "description", "path", "type", "allowed_extensions"]);
        var path = root.GetProperty("path").GetString();
        var allowed = root.GetProperty("allowed_extensions");
        return root.GetProperty("name").GetString() == HostName && root.GetProperty("type").GetString() == "stdio"
            && path is not null && Path.IsPathFullyQualified(path)
            && string.Equals(Path.GetFullPath(path), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase)
            && allowed.ValueKind == JsonValueKind.Array && allowed.GetArrayLength() == 1
            && allowed[0].GetString() == ExtensionId;
    }

    internal static BrowserProbeRequest ParseObservation(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
        var root = doc.RootElement;
        Exact(root, ["version", "command", "windowId", "nonce", "left", "top", "width", "height"]);
        var id = root.GetProperty("windowId").GetInt32();
        var nonce = root.GetProperty("nonce").GetString();
        var left = root.GetProperty("left").GetInt32();
        var top = root.GetProperty("top").GetInt32();
        var width = root.GetProperty("width").GetInt32();
        var height = root.GetProperty("height").GetInt32();
        if (root.GetProperty("version").GetInt32() != 1 || root.GetProperty("command").GetString() != "observe"
            || id < 0 || nonce is not { Length: 32 } || !nonce.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')
            || width <= 0 || height <= 0) throw new InvalidDataException("Invalid Firefox observation.");
        return new(id, nonce, new(left, top, checked(left + width), checked(top + height)));
    }

    private static void Exact(JsonElement root, string[] fields)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Expected object.");
        var names = root.EnumerateObject().Select(p => p.Name).ToArray();
        if (names.Length != fields.Length || names.Distinct(StringComparer.Ordinal).Count() != fields.Length
            || names.Any(n => !fields.Contains(n, StringComparer.Ordinal)))
            throw new InvalidDataException("Unexpected Firefox diagnostic fields.");
    }
}

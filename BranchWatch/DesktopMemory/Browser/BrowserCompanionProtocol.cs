using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace BranchWatch;

internal sealed record CompanionHello(int Version, string Command, string ProfileId, string Nonce);
internal sealed record CompanionReply(int Version, string Status, string ProfileId, string Nonce);

internal static class BrowserCompanionProtocol
{
    internal const int MaximumBytes = 1024;
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    internal static string PipeName
    {
        get
        {
            using var process = Process.GetCurrentProcess();
            return $"BranchWatch-Browser-v1-{BranchWatchApplicationIdentity.UserHash}-{process.SessionId}";
        }
    }
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    internal static CompanionHello ParseHello(byte[] bytes)
    {
        using var doc = Parse(bytes, ["version", "command", "profileId", "nonce"]);
        var root = doc.RootElement;
        var version = root.GetProperty("version").GetInt32();
        var command = root.GetProperty("command").GetString();
        var profile = root.GetProperty("profileId").GetString();
        var nonce = root.GetProperty("nonce").GetString();
        if (version != 1 || command != "hello" || !ValidId(profile) || !ValidId(nonce))
            throw new InvalidDataException("Unsupported companion request.");
        return new(version, command, profile!, nonce!);
    }

    internal static CompanionReply ParseReply(byte[] bytes, CompanionHello request)
    {
        using var doc = Parse(bytes, ["version", "status", "profileId", "nonce"]);
        var root = doc.RootElement;
        var version = root.GetProperty("version").GetInt32();
        var status = root.GetProperty("status").GetString();
        var profile = root.GetProperty("profileId").GetString();
        var nonce = root.GetProperty("nonce").GetString();
        if (version != 1 || status != "connected" || profile != request.ProfileId || nonce != request.Nonce)
            throw new InvalidDataException("Companion reply did not match request.");
        return new(version, status, profile!, nonce!);
    }

    private static JsonDocument Parse(byte[] bytes, string[] expected)
    {
        if (bytes.Length == 0 || bytes.Length > MaximumBytes) throw new InvalidDataException("Invalid message size.");
        var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 3 });
        if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            var names = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
            if (names.Length == expected.Length && names.Distinct(StringComparer.Ordinal).Count() == expected.Length
                && names.All(n => expected.Contains(n, StringComparer.Ordinal))) return doc;
        }
        doc.Dispose();
        throw new InvalidDataException("Unexpected message fields.");
    }

    private static bool ValidId(string? value) =>
        value is { Length: 32 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static byte[] Encode<T>(T message) => JsonSerializer.SerializeToUtf8Bytes(message, Options);

    internal static async Task<byte[]> ReadAsync(Stream stream, CancellationToken cancellation)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellation).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (length == 0 || length > MaximumBytes) throw new InvalidDataException("Invalid message length.");
        var bytes = new byte[(int)length];
        await stream.ReadExactlyAsync(bytes, cancellation).ConfigureAwait(false);
        return bytes;
    }

    internal static async Task WriteAsync(Stream stream, byte[] bytes, CancellationToken cancellation)
    {
        if (bytes.Length == 0 || bytes.Length > MaximumBytes) throw new InvalidDataException("Invalid message length.");
        var header = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)bytes.Length);
        await stream.WriteAsync(header, cancellation).ConfigureAwait(false);
        await stream.WriteAsync(bytes, cancellation).ConfigureAwait(false);
        await stream.FlushAsync(cancellation).ConfigureAwait(false);
    }

    internal static async Task<CompanionReply> ConnectAsync(string name, CompanionHello request, CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(Timeout);
        await using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, System.Security.Principal.TokenImpersonationLevel.Anonymous);
        await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);
        await WriteAsync(pipe, Encode(request), deadline.Token).ConfigureAwait(false);
        return ParseReply(await ReadAsync(pipe, deadline.Token).ConfigureAwait(false), request);
    }
}

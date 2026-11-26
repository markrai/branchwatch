using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BranchWatch;

internal interface IDesktopSnapshotProtector
{
    byte[] Protect(byte[] data);
    byte[] Unprotect(byte[] data);
}

internal sealed class CurrentUserSnapshotProtector : IDesktopSnapshotProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("BranchWatch.DesktopMemory.Snapshot.v2");

    public byte[] Protect(byte[] data) => ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
    public byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
}

internal sealed record ProtectedDesktopSnapshotFile(int SchemaVersion, string Protection, byte[] Payload);
internal sealed record DecodedDesktopSnapshotFile(IReadOnlyList<DesktopSnapshot> Snapshots, bool IsProtected);

internal sealed class DesktopSnapshotProtection(IDesktopSnapshotProtector protector)
{
    private const int LegacyVersion = 1;
    private const int ProtectedVersion = 2;
    private const string Scheme = "DPAPI-CurrentUser";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    internal DecodedDesktopSnapshotFile Decode(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid desktop snapshot file.");
        var names = root.EnumerateObject().Select(p => p.Name).ToArray();
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length
            || !root.TryGetProperty("SchemaVersion", out var version) || !version.TryGetInt32(out var schema))
            throw new InvalidDataException("Invalid snapshot schema.");
        if (schema == LegacyVersion)
        {
            RequireFields(names, ["SchemaVersion", "Snapshots"]);
            return new(ParseInner(bytes), false);
        }
        if (schema != ProtectedVersion) throw new InvalidDataException("Unsupported desktop snapshot format.");
        RequireFields(names, ["SchemaVersion", "Protection", "Payload"]);
        var outer = root.Deserialize<ProtectedDesktopSnapshotFile>()
            ?? throw new InvalidDataException("Invalid protected snapshot.");
        if (outer.Protection != Scheme || outer.Payload is not { Length: > 0 })
            throw new InvalidDataException("Unsupported snapshot protection.");
        byte[] plaintext;
        try { plaintext = protector.Unprotect(outer.Payload); }
        catch (CryptographicException)
        {
            throw new InvalidDataException("Snapshot could not be decrypted by the current Windows user.");
        }
        try { return new(ParseInner(plaintext), true); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    internal byte[] Encode(IReadOnlyList<DesktopSnapshot> snapshots)
    {
        Validate(snapshots);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(new DesktopSnapshotFile(LegacyVersion, snapshots), Options);
        try
        {
            var payload = protector.Protect(plaintext);
            if (payload.Length == 0) throw new CryptographicException("Snapshot protection returned an empty payload.");
            return JsonSerializer.SerializeToUtf8Bytes(new ProtectedDesktopSnapshotFile(ProtectedVersion, Scheme, payload), Options);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    private static IReadOnlyList<DesktopSnapshot> ParseInner(byte[] data)
    {
        var file = JsonSerializer.Deserialize<DesktopSnapshotFile>(data)
            ?? throw new InvalidDataException("Empty desktop snapshot file.");
        if (file.SchemaVersion != LegacyVersion || file.Snapshots is null)
            throw new InvalidDataException("Unsupported desktop snapshot payload.");
        Validate(file.Snapshots);
        return file.Snapshots;
    }

    private static void Validate(IReadOnlyList<DesktopSnapshot> snapshots)
    {
        var ids = new HashSet<Guid>();
        foreach (var snapshot in snapshots)
        {
            DesktopSnapshotValidation.Validate(snapshot);
            if (!ids.Add(snapshot.DesktopId)) throw new InvalidDataException("Duplicate desktop snapshot.");
        }
    }

    private static void RequireFields(string[] actual, string[] expected)
    {
        if (actual.Length != expected.Length || actual.Any(n => !expected.Contains(n, StringComparer.Ordinal)))
            throw new InvalidDataException("Unexpected snapshot envelope fields.");
    }
}

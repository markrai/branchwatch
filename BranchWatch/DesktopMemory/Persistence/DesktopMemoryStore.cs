using System.IO;
using System.Security.Cryptography;

namespace BranchWatch;

internal enum DesktopSnapshotStorageState { Empty, Protected, Legacy }

internal sealed class DesktopMemoryStore(string path, IDesktopSnapshotProtector? protector = null)
{
    private static readonly object Gate = new();
    private readonly DesktopSnapshotProtection _protection = new(protector ?? new CurrentUserSnapshotProtector());
    private string PrimaryPath => Path.GetFullPath(path);
    private string BackupPath => Path.Combine(Path.GetDirectoryName(PrimaryPath)!, "snapshots.backup.json");

    internal IReadOnlyList<DesktopSnapshot> Load()
    {
        lock (Gate) return Read(PrimaryPath)?.Snapshots ?? [];
    }

    internal DesktopSnapshotStorageState GetStorageState()
    {
        lock (Gate)
        {
            var primary = Read(PrimaryPath);
            var backup = Read(BackupPath);
            if (primary is null && backup is null) return DesktopSnapshotStorageState.Empty;
            return primary is { IsProtected: false } || backup is { IsProtected: false }
                ? DesktopSnapshotStorageState.Legacy : DesktopSnapshotStorageState.Protected;
        }
    }

    internal void ProtectLegacyFiles()
    {
        lock (Gate) ProtectExisting(Read(PrimaryPath), Read(BackupPath));
    }

    internal void Save(DesktopSnapshot snapshot, DateTimeOffset? expectedCapture)
    {
        lock (Gate)
        {
            DesktopSnapshotValidation.Validate(snapshot);
            if (snapshot.Windows.Count == 0) throw new InvalidOperationException("An empty capture cannot be saved.");
            var prior = Read(PrimaryPath);
            var snapshots = prior?.Snapshots.ToList() ?? [];
            var existing = snapshots.SingleOrDefault(s => s.DesktopId == snapshot.DesktopId);
            if (existing?.CapturedUtc != expectedCapture)
                throw new InvalidOperationException("The saved snapshot changed. Refresh before replacing it.");
            if (existing is not null) snapshots.Remove(existing);
            snapshots.Add(snapshot);
            Write(snapshots, prior);
        }
    }

    internal void Delete(Guid desktopId, DateTimeOffset expectedCapture)
    {
        lock (Gate)
        {
            var prior = Read(PrimaryPath);
            var snapshots = prior?.Snapshots.ToList() ?? [];
            var existing = snapshots.SingleOrDefault(s => s.DesktopId == desktopId);
            if (existing is null || existing.CapturedUtc != expectedCapture)
                throw new InvalidOperationException("The saved snapshot changed. Refresh before deleting it.");
            snapshots.Remove(existing);
            Write(snapshots, prior);
        }
    }

    private DecodedDesktopSnapshotFile? Read(string file)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(file); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        try { return _protection.Decode(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private void ProtectExisting(DecodedDesktopSnapshotFile? primary, DecodedDesktopSnapshotFile? backup)
    {
        var protectedPrimary = primary is { IsProtected: false } ? _protection.Encode(primary.Snapshots) : null;
        var protectedBackup = backup is { IsProtected: false } ? _protection.Encode(backup.Snapshots) : null;
        if (protectedBackup is not null) Replace(BackupPath, protectedBackup, null);
        if (protectedPrimary is not null) Replace(PrimaryPath, protectedPrimary, null);
    }

    private void Write(IReadOnlyList<DesktopSnapshot> snapshots, DecodedDesktopSnapshotFile? prior)
    {
        var next = _protection.Encode(snapshots);
        ProtectExisting(prior, Read(BackupPath));
        Replace(PrimaryPath, next, prior is null ? null : BackupPath);
    }

    private static void Replace(string destination, byte[] protectedBytes, string? backup)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(protectedBytes);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(destination)) File.Replace(temporary, destination, backup);
            else File.Move(temporary, destination);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

using System.IO;
using System.Text.Json;

namespace BranchWatch;

internal sealed class DesktopMemoryStore(string path)
{
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    internal IReadOnlyList<DesktopSnapshot> Load()
    {
        FileStream stream;
        try { stream = File.OpenRead(path); }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }

        using (stream)
        {
            var file = JsonSerializer.Deserialize<DesktopSnapshotFile>(stream, Options)
                ?? throw new InvalidDataException("Empty desktop snapshot file.");
            if (file.SchemaVersion != SchemaVersion || file.Snapshots is null)
                throw new InvalidDataException("Unsupported desktop snapshot format.");
            var ids = new HashSet<Guid>();
            foreach (var snapshot in file.Snapshots)
            {
                DesktopSnapshotValidation.Validate(snapshot);
                if (!ids.Add(snapshot.DesktopId)) throw new InvalidDataException("Duplicate desktop snapshot.");
            }
            return file.Snapshots;
        }
    }

    internal void Save(DesktopSnapshot snapshot, DateTimeOffset? expectedCapture)
    {
        DesktopSnapshotValidation.Validate(snapshot);
        if (snapshot.Windows.Count == 0) throw new InvalidOperationException("An empty capture cannot be saved.");
        var snapshots = Load().ToList();
        var existing = snapshots.SingleOrDefault(s => s.DesktopId == snapshot.DesktopId);
        if (existing?.CapturedUtc != expectedCapture)
            throw new InvalidOperationException("The saved snapshot changed. Refresh before replacing it.");
        if (existing is not null) snapshots.Remove(existing);
        snapshots.Add(snapshot);
        Write(snapshots);
    }

    internal void Delete(Guid desktopId, DateTimeOffset expectedCapture)
    {
        var snapshots = Load().ToList();
        var existing = snapshots.SingleOrDefault(s => s.DesktopId == desktopId);
        if (existing is null || existing.CapturedUtc != expectedCapture)
            throw new InvalidOperationException("The saved snapshot changed. Refresh before deleting it.");
        snapshots.Remove(existing);
        Write(snapshots);
    }

    private void Write(IReadOnlyList<DesktopSnapshot> snapshots)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var backup = Path.Combine(directory, "snapshots.backup.json");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new DesktopSnapshotFile(SchemaVersion, snapshots), Options);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(fullPath)) File.Replace(temporary, fullPath, backup);
            else File.Move(temporary, fullPath);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

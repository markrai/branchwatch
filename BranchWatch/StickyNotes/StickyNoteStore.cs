using System.IO;
using System.Text.Json;

namespace BranchWatch;

internal sealed class StickyNoteStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    internal List<StickyNote> Load()
    {
        FileStream stream;
        try
        {
            stream = File.OpenRead(path);
        }
        catch (FileNotFoundException)
        {
            return [];
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }

        using var input = stream;
        var notes = JsonSerializer.Deserialize<List<StickyNote>>(input, Options)
            ?? throw new InvalidDataException("Invalid sticky notes file.");

        var ids = new HashSet<Guid>();
        foreach (var note in notes)
        {
            if (note is null || note.Id == Guid.Empty || note.DesktopId == Guid.Empty
                || note.Text is null || !ids.Add(note.Id))
            {
                throw new InvalidDataException("Invalid sticky note identity or content.");
            }
        }

        return notes;
    }

    internal void Save(IReadOnlyList<StickyNote> notes)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write))
            {
                JsonSerializer.Serialize(stream, notes, Options);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}

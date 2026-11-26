using System.IO;
using System.Text.Json;

namespace BranchWatch;

internal static class DesktopMemoryProbeCommand
{
    internal const string Command = "--desktop-memory-probe";
    internal const string IncludeIdentities = "--include-identities";

    internal static bool TryExecute(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0 || !string.Equals(args[0], Command, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!TryParse(args, out var includeIdentities))
        {
            exitCode = 2;
            return true;
        }

        string? temporary = null;
        string? failurePath = null;
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BranchWatch", "desktop-memory");
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, $"desktop-memory-probe-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
            temporary = file + ".tmp";
            failurePath = file + ".error.json";
            var report = DesktopMemoryProbe.Capture(includeIdentities);
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(output, report, new JsonSerializerOptions { WriteIndented = true });
                output.Flush(flushToDisk: true);
            }

            File.Move(temporary, file);
            temporary = null;
        }
        catch (Exception ex)
        {
            exitCode = 1;
            Console.Error.WriteLine($"Desktop Memory qualification failed (HRESULT 0x{ex.HResult:X8}).");
            if (failurePath is not null)
            {
                try
                {
                    File.WriteAllText(failurePath, JsonSerializer.Serialize(new
                    {
                        FailedUtc = DateTimeOffset.UtcNow,
                        ExceptionType = ex.GetType().FullName,
                        ex.HResult
                    }));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        return true;
    }

    internal static bool TryParse(string[] args, out bool includeIdentities)
    {
        includeIdentities = false;
        if (args.Length == 0 || !string.Equals(args[0], Command, StringComparison.OrdinalIgnoreCase)) return false;
        if (args.Length == 1) return true;
        if (args.Length != 2 || !string.Equals(args[1], IncludeIdentities, StringComparison.OrdinalIgnoreCase)) return false;
        includeIdentities = true;
        return true;
    }
}

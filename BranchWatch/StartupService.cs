using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace BranchWatch;

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BranchWatch";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (key is null)
        {
            throw new InvalidOperationException("Unable to open the current user Windows startup registry key.");
        }

        if (enabled)
        {
            key.SetValue(ValueName, GetStartupCommand(), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static string GetStartupCommand()
    {
        var processPath = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName;

        if (!string.IsNullOrWhiteSpace(processPath)
            && string.Equals(Path.GetFileName(processPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            var assemblyName = Assembly.GetEntryAssembly()?.GetName().Name;
            if (!string.IsNullOrWhiteSpace(assemblyName))
            {
                var entryDllPath = Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.dll");
                if (File.Exists(entryDllPath))
                {
                    return $"\"{processPath}\" \"{entryDllPath}\"";
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(processPath))
        {
            return $"\"{processPath}\"";
        }

        throw new InvalidOperationException("Unable to determine the BranchWatch executable path.");
    }
}

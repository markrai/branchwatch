# BranchWatch

This is a Windows tray app which shows the current Git repo + branch of a selected repository in a  customizable on-screen overlay. You are free to repurpose this, or expand upon it, as you wish. Any useful additions to the existing features set are welcome.

## Requirements

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (build only)

## Build

```bat
build.bat
```

Output: `publish\win-x64\BranchWatch.exe` (self-contained, no runtime install needed)

## Run from source

```bat
dotnet run --project BranchWatch\BranchWatch.csproj
```

## Settings

Stored per user at:

`%AppData%\BranchWatch\settings.json`

Example: `C:\Users\<you>\AppData\Roaming\BranchWatch\settings.json`

Includes repository path, overlay position, opacity, outline, font color, and visibility.

**Start with Windows** is stored in the registry:

`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → `BranchWatch`

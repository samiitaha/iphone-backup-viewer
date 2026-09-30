# iPhone Backup Viewer

A Windows desktop app for inspecting iPhone backup folders, especially large backup directories with many files.

This project is designed for legitimate file and metadata inspection of a backup directory you own or have explicit permission to analyze. It can:

- open a backup folder
- detect whether the folder appears to be encrypted using `Status.plist`
- read `Manifest.db` when present and list files from the Apple manifest
- recursively scan a normal backup folder when a manifest is unavailable
- view file metadata such as path, type, size, and last modified time
- show summary information in a Windows desktop UI

Important note:
- This app does not bypass Apple encryption or recover private data without access to the proper credentials.
- For encrypted backups, the app is intentionally limited to safe status detection and metadata display unless the required backup credentials are provided by the authorized owner.

## Requirements

- Windows 10 or Windows 11
- .NET 8 SDK

## Build

```bash
dotnet restore

dotnet build BackupViewerApp.sln
```

## Run

```bash
dotnet run --project BackupViewerApp/BackupViewerApp.csproj
```

## Notes

The application is structured as a .NET WPF desktop app so it can run natively on Windows.

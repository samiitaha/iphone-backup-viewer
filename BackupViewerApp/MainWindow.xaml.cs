using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;

namespace BackupViewerApp;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly BackupScannerService _scanner = new();

    public ObservableCollection<BackupFileEntry> Files { get; } = new();

    private string _backupPath = "No backup folder selected";
    public string BackupPath
    {
        get => _backupPath;
        set
        {
            if (_backupPath == value) return;
            _backupPath = value;
            OnPropertyChanged();
        }
    }

    private string _statusText = "Ready";
    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText == value) return;
            _statusText = value;
            OnPropertyChanged();
        }
    }

    private string _detailsText = "Select a file to inspect details.";
    public string DetailsText
    {
        get => _detailsText;
        set
        {
            if (_detailsText == value) return;
            _detailsText = value;
            OnPropertyChanged();
        }
    }

    private int _fileCount;
    public int FileCount
    {
        get => _fileCount;
        set
        {
            if (_fileCount == value) return;
            _fileCount = value;
            OnPropertyChanged();
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        RefreshFileList();
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select an iPhone backup folder",
            ShowNewFolderButton = false
        };

        var result = dialog.ShowDialog();
        if (result == System.Windows.Forms.DialogResult.OK)
        {
            LoadBackup(dialog.SelectedPath);
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(BackupPath) && BackupPath != "No backup folder selected")
        {
            LoadBackup(BackupPath);
        }
        else
        {
            StatusText = "Select a backup folder first.";
        }
    }

    private void LoadBackup(string backupRoot)
    {
        try
        {
            if (!Directory.Exists(backupRoot))
            {
                throw new DirectoryNotFoundException($"Backup directory not found: {backupRoot}");
            }

            var scanResult = _scanner.Scan(backupRoot);

            Files.Clear();
            foreach (var file in scanResult.Files)
            {
                Files.Add(file);
            }

            BackupPath = scanResult.BackupRoot;
            StatusText = scanResult.StatusText;
            FileCount = scanResult.Files.Count;
            DetailsText = $"Backup root: {scanResult.BackupRoot}\nEncrypted: {scanResult.IsEncrypted}\nFiles discovered: {scanResult.Files.Count}";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            DetailsText = ex.ToString();
        }
    }

    private void FileGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FileGrid.SelectedItem is not BackupFileEntry selected)
        {
            DetailsText = "Select a file to inspect details.";
            return;
        }

        DetailsText = $"Path: {selected.RelativePath}\n" +
                      $"Full path: {selected.FullPath}\n" +
                      $"Size: {selected.SizeDisplay}\n" +
                      $"Type: {selected.Type}\n" +
                      $"Encrypted: {selected.IsEncrypted}\n" +
                      $"Status: {selected.Status}\n" +
                      $"Last modified: {selected.LastModified}";
    }

    private void RefreshFileList()
    {
        Files.Clear();
        FileCount = 0;
        DetailsText = "Select a backup folder to begin.";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class BackupScanResult
{
    public string BackupRoot { get; set; } = string.Empty;
    public bool IsEncrypted { get; set; }
    public string StatusText { get; set; } = "Ready";
    public List<BackupFileEntry> Files { get; set; } = new();
}

public sealed class BackupFileEntry
{
    public string RelativePath { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string Type { get; set; } = "Unknown";
    public bool IsEncrypted { get; set; }
    public string Status { get; set; } = "Unknown";
    public string SizeDisplay { get; set; } = "0 B";
    public string LastModified { get; set; } = "N/A";
    public long SizeBytes { get; set; }
}

public sealed class BackupScannerService
{
    public BackupScanResult Scan(string backupRoot)
    {
        var result = new BackupScanResult
        {
            BackupRoot = backupRoot,
            StatusText = "Backup loaded"
        };

        var statusInfo = ReadStatusPlist(backupRoot);
        result.IsEncrypted = statusInfo.IsEncrypted;
        result.StatusText = result.IsEncrypted
            ? "Encrypted backup detected"
            : "Unencrypted backup or metadata not present";

        var files = new List<BackupFileEntry>();

        if (File.Exists(Path.Combine(backupRoot, "Manifest.db")))
        {
            files.AddRange(ReadManifestDatabase(Path.Combine(backupRoot, "Manifest.db"), backupRoot));
        }

        if (files.Count == 0)
        {
            files.AddRange(EnumerateDirectory(backupRoot));
        }

        result.Files = files.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
        return result;
    }

    private static StatusInfo ReadStatusPlist(string backupRoot)
    {
        var statusPath = Path.Combine(backupRoot, "Status.plist");
        if (!File.Exists(statusPath))
        {
            return new StatusInfo
            {
                IsEncrypted = false,
                Message = "No Status.plist file found."
            };
        }

        try
        {
            var content = File.ReadAllText(statusPath);
            return content.Contains("IsEncrypted", StringComparison.OrdinalIgnoreCase)
                ? new StatusInfo { IsEncrypted = content.Contains("true", StringComparison.OrdinalIgnoreCase), Message = "Status.plist found." }
                : new StatusInfo { IsEncrypted = false, Message = "Status.plist does not declare encryption." };
        }
        catch
        {
            return new StatusInfo { IsEncrypted = false, Message = "Unable to read Status.plist." };
        }
    }

    private static IEnumerable<BackupFileEntry> ReadManifestDatabase(string manifestDbPath, string backupRoot)
    {
        var entries = new List<BackupFileEntry>();

        try
        {
            using var connection = new SqliteConnection($"Data Source={manifestDbPath}");
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM Files LIMIT 10000";

            using var reader = command.ExecuteReader();
            var fieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                fieldNames.Add(reader.GetName(i));
            }

            while (reader.Read())
            {
                var relativePath = GetValue(reader, fieldNames, "relativePath", "filePath", "path");
                var domain = GetValue(reader, fieldNames, "domain");
                var fileId = GetValue(reader, fieldNames, "fileID", "id");
                var size = GetLongValue(reader, fieldNames, "size");
                var flags = GetValue(reader, fieldNames, "flags");
                var lastModified = GetValue(reader, fieldNames, "mtime");

                var path = !string.IsNullOrWhiteSpace(relativePath)
                    ? relativePath
                    : (domain != null && fileId != null ? $"{domain}/{fileId}" : "unknown");

                entries.Add(new BackupFileEntry
                {
                    RelativePath = path,
                    FullPath = Path.Combine(backupRoot, path),
                    Type = DetermineType(path),
                    IsEncrypted = true,
                    Status = string.IsNullOrWhiteSpace(flags) ? "Manifest record" : $"Flags: {flags}",
                    SizeBytes = size,
                    SizeDisplay = FormatSize(size),
                    LastModified = string.IsNullOrWhiteSpace(lastModified) ? "N/A" : lastModified
                });
            }
        }
        catch
        {
            // Fall back to directory scanning when Manifest.db is unreadable.
        }

        return entries;
    }

    private static IEnumerable<BackupFileEntry> EnumerateDirectory(string backupRoot)
    {
        var results = new List<BackupFileEntry>();

        foreach (var file in Directory.EnumerateFiles(backupRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(backupRoot, file);
            var fi = new FileInfo(file);
            results.Add(new BackupFileEntry
            {
                RelativePath = relativePath.Replace('\\', '/'),
                FullPath = file,
                Type = DetermineType(relativePath),
                IsEncrypted = false,
                Status = "File detected in directory scan",
                SizeBytes = fi.Length,
                SizeDisplay = FormatSize(fi.Length),
                LastModified = fi.LastWriteTimeUtc.ToString("O")
            });
        }

        return results;
    }

    private static string GetValue(SqliteDataReader reader, HashSet<string> fieldNames, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (fieldNames.Contains(candidate) && !reader.IsDBNull(reader.GetOrdinal(candidate)))
            {
                return reader[candidate]?.ToString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static long GetLongValue(SqliteDataReader reader, HashSet<string> fieldNames, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (fieldNames.Contains(candidate) && !reader.IsDBNull(reader.GetOrdinal(candidate)))
            {
                var value = reader[candidate];
                if (value is long l) return l;
                if (value is int i) return i;
                if (value is string s && long.TryParse(s, out var parsed)) return parsed;
            }
        }

        return 0;
    }

    private static string DetermineType(string relativePath)
    {
        var lower = relativePath.ToLowerInvariant();
        if (lower.EndsWith(".db")) return "SQLite DB";
        if (lower.EndsWith(".plist")) return "Property List";
        if (lower.EndsWith(".jpg") || lower.EndsWith(".jpeg") || lower.EndsWith(".png") || lower.EndsWith(".heic")) return "Image";
        if (lower.EndsWith(".mp4") || lower.EndsWith(".mov")) return "Video";
        if (lower.EndsWith(".mp3") || lower.EndsWith(".m4a")) return "Audio";
        if (lower.EndsWith(".db-wal") || lower.EndsWith("-wal")) return "Write-Ahead Log";
        return "Generic File";
    }

    private static string FormatSize(long bytes)
    {
        const double divisor = 1024d;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        var unitIndex = 0;

        while (size >= divisor && unitIndex < units.Length - 1)
        {
            size /= divisor;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{bytes} {units[unitIndex]}"
            : $"{size:0.##} {units[unitIndex]}";
    }

    private sealed class StatusInfo
    {
        public bool IsEncrypted { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}

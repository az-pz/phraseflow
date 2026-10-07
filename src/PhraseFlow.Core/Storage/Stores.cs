using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using PhraseFlow.Core.Models;

namespace PhraseFlow.Core.Storage;

public static class JsonConfig
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}

/// <summary>Locations of PhraseFlow's data files.</summary>
public sealed record DataPaths(string Root)
{
    public const string PortableMarker = "portable.txt";

    public string LibraryFile => Path.Combine(Root, "library.json");

    public string SettingsFile => Path.Combine(Root, "settings.json");

    public string BackupDirectory => Path.Combine(Root, "Backups");

    public string LogFile => Path.Combine(Root, "phraseflow.log");

    /// <summary>Uses a <c>Data</c> folder next to the executable when <c>portable.txt</c> exists, otherwise %APPDATA%\PhraseFlow.</summary>
    public static DataPaths Resolve(string applicationDirectory)
    {
        var root = File.Exists(Path.Combine(applicationDirectory, PortableMarker))
            ? Path.Combine(applicationDirectory, "Data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhraseFlow");
        return new DataPaths(root);
    }
}

public static class AtomicFile
{
    public static void WriteAllText(string path, string content)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, path, overwrite: true);
    }
}

public sealed class LibraryStore(string filePath, string backupDirectory)
{
    public string FilePath { get; } = filePath;

    public string BackupDirectory { get; } = backupDirectory;

    public bool Exists => File.Exists(FilePath);

    public static string Serialize(SnippetLibrary library) => JsonSerializer.Serialize(library, JsonConfig.Options);

    public static SnippetLibrary Deserialize(string json)
    {
        var library = JsonSerializer.Deserialize<SnippetLibrary>(json, JsonConfig.Options)
                      ?? throw new InvalidDataException("The library file is empty.");
        library.Normalize();
        return library;
    }

    public SnippetLibrary Load() => Deserialize(File.ReadAllText(FilePath));

    public void Save(SnippetLibrary library) => AtomicFile.WriteAllText(FilePath, Serialize(library));

    /// <summary>Copies the current library file into the backup folder and prunes old backups.</summary>
    public string? CreateBackup(int keep)
    {
        if (!Exists || keep <= 0)
        {
            return null;
        }

        Directory.CreateDirectory(BackupDirectory);
        var target = Path.Combine(BackupDirectory, $"library-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.Copy(FilePath, target, overwrite: true);
        foreach (var old in ListBackups().Skip(keep))
        {
            try
            {
                old.Delete();
            }
            catch (IOException)
            {
                // A locked backup is not worth failing over.
            }
        }

        return target;
    }

    /// <summary>Backups, newest first.</summary>
    public IReadOnlyList<FileInfo> ListBackups()
    {
        if (!Directory.Exists(BackupDirectory))
        {
            return [];
        }

        return new DirectoryInfo(BackupDirectory)
            .GetFiles("library-*.json")
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .ToList();
    }
}

public sealed class SettingsStore(string filePath)
{
    public string FilePath { get; } = filePath;

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonConfig.Options);
                if (settings is not null)
                {
                    settings.Normalize();
                    return settings;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Fall back to defaults for unreadable settings.
        }

        return new AppSettings();
    }

    public void Save(AppSettings settings) =>
        AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonConfig.Options));
}

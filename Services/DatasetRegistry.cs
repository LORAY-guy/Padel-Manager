using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Padel.Manager.Services;

public sealed class DatasetEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("lastOpenedUtc")]
    public DateTime LastOpenedUtc { get; set; }
}

public sealed class DatasetRegistry
{
    private const string AppFolderName = "PadelManager";
    private const string RegistryFileName = "registry.json";
    private const string LegacyFileName = "PadelProgramme.padel";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly List<DatasetEntry> _datasets;

    private DatasetRegistry(string registryPath, string defaultDatasetFolder, List<DatasetEntry> datasets, string? lastOpenedId)
    {
        RegistryPath = registryPath;
        DefaultDatasetFolder = defaultDatasetFolder;
        _datasets = datasets;
        LastOpenedId = lastOpenedId;
    }

    public string RegistryPath { get; }
    public string DefaultDatasetFolder { get; }
    public string? LastOpenedId { get; private set; }
    public IReadOnlyList<DatasetEntry> Datasets => _datasets;

    public static DatasetRegistry Load()
    {
        var configDir = GetConfigDir();
        var registryPath = System.IO.Path.Combine(configDir, RegistryFileName);
        var defaultDatasetFolder = GetDefaultDatasetFolder();

        Directory.CreateDirectory(configDir);
        Directory.CreateDirectory(defaultDatasetFolder);

        List<DatasetEntry> datasets;
        string? lastOpenedId = null;

        if (File.Exists(registryPath))
        {
            try
            {
                var json = File.ReadAllText(registryPath);
                var stored = JsonSerializer.Deserialize<StoredRegistry>(json, JsonOptions);
                datasets = stored?.Datasets ?? new List<DatasetEntry>();
                lastOpenedId = stored?.LastOpenedId;
            }
            catch
            {
                datasets = new List<DatasetEntry>();
            }
        }
        else
        {
            datasets = new List<DatasetEntry>();
        }

        // Auto-migrate legacy PadelProgramme.padel from Documents if not yet registered
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(docs))
        {
            var legacy = System.IO.Path.Combine(docs, LegacyFileName);
            if (File.Exists(legacy) && datasets.All(d => !string.Equals(d.Path, legacy, StringComparison.OrdinalIgnoreCase)))
            {
                var legacyEntry = new DatasetEntry
                {
                    Name = "PadelProgramme",
                    Path = legacy,
                    LastOpenedUtc = File.GetLastWriteTimeUtc(legacy)
                };
                datasets.Insert(0, legacyEntry);
                lastOpenedId ??= legacyEntry.Id;
            }
        }

        var registry = new DatasetRegistry(registryPath, defaultDatasetFolder, datasets, lastOpenedId);
        if (datasets.Count > 0)
        {
            registry.Save();
        }

        return registry;
    }

    public DatasetEntry? GetLastOpened()
    {
        if (LastOpenedId is null)
        {
            return null;
        }

        return _datasets.FirstOrDefault(d => d.Id == LastOpenedId);
    }

    public void AddOrUpdate(DatasetEntry entry)
    {
        var existing = _datasets.FirstOrDefault(d => d.Id == entry.Id);
        if (existing is not null)
        {
            existing.Name = entry.Name;
            existing.Path = entry.Path;
            existing.LastOpenedUtc = entry.LastOpenedUtc;
        }
        else
        {
            _datasets.Add(entry);
        }
    }

    public void Remove(string id)
    {
        _datasets.RemoveAll(d => d.Id == id);
        if (LastOpenedId == id)
        {
            LastOpenedId = _datasets.Count > 0 ? _datasets[0].Id : null;
        }
    }

    public void Rename(string id, string newName)
    {
        var entry = _datasets.FirstOrDefault(d => d.Id == id);
        if (entry is not null)
        {
            entry.Name = newName;
        }
    }

    public void SetLastOpened(string id)
    {
        LastOpenedId = id;
        var entry = _datasets.FirstOrDefault(d => d.Id == id);
        if (entry is not null)
        {
            entry.LastOpenedUtc = DateTime.UtcNow;
        }
    }

    public DatasetEntry? TryGetByPath(string path)
        => _datasets.FirstOrDefault(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase));

    public string BuildNewDatasetPath(string name)
    {
        var safeName = SanitizeFileName(name);
        var basePath = System.IO.Path.Combine(DefaultDatasetFolder, $"{safeName}.padel");
        if (!File.Exists(basePath))
        {
            return basePath;
        }

        for (var i = 2; i < 100; i++)
        {
            var candidate = System.IO.Path.Combine(DefaultDatasetFolder, $"{safeName}_{i}.padel");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return System.IO.Path.Combine(DefaultDatasetFolder, $"{safeName}_{Guid.NewGuid():N}.padel");
    }

    public void Save()
    {
        var stored = new StoredRegistry { Datasets = _datasets, LastOpenedId = LastOpenedId };
        var json = JsonSerializer.Serialize(stored, JsonOptions);
        File.WriteAllText(RegistryPath, json);
    }

    private static string GetConfigDir()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
        {
            return System.IO.Path.Combine(appData, AppFolderName);
        }

        return System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            $".{AppFolderName}");
    }

    private static string GetDefaultDatasetFolder()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(docs))
        {
            return System.IO.Path.Combine(docs, AppFolderName);
        }

        return System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            AppFolderName);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Dataset" : sanitized;
    }

    private sealed class StoredRegistry
    {
        [JsonPropertyName("datasets")]
        public List<DatasetEntry> Datasets { get; set; } = new();

        [JsonPropertyName("lastOpenedId")]
        public string? LastOpenedId { get; set; }
    }
}

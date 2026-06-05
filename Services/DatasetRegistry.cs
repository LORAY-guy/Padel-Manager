using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Padel.Manager.Services;

public enum DatasetSource
{
    Local = 0,
    Server = 1
}

public sealed class DatasetEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>For local datasets, the .padel file. For server datasets, the local cache file.</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("lastOpenedUtc")]
    public DateTime LastOpenedUtc { get; set; }

    [JsonPropertyName("source")]
    public DatasetSource Source { get; set; } = DatasetSource.Local;

    /// <summary>Server-side dataset id (server datasets only).</summary>
    [JsonPropertyName("serverId")]
    public string? ServerId { get; set; }

    /// <summary>Last version synced from/to the server (server datasets only).</summary>
    [JsonPropertyName("serverVersion")]
    public int ServerVersion { get; set; }

    [JsonIgnore]
    public bool IsServer => Source == DatasetSource.Server;
}

public sealed class DatasetRegistry
{
    private const string AppFolderName = "PadelManager";
    private const string RegistryFileName = "registry.json";
    private const string LegacyFileName = "PadelProgramme.padel";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly List<DatasetEntry> _datasets;

    private DatasetRegistry(string registryPath, string defaultDatasetFolder, List<DatasetEntry> datasets, string? lastOpenedId, bool darkMode, string? serverUrl, string? serverToken)
    {
        RegistryPath = registryPath;
        DefaultDatasetFolder = defaultDatasetFolder;
        _datasets = datasets;
        LastOpenedId = lastOpenedId;
        DarkMode = darkMode;
        ServerUrl = serverUrl;
        ServerToken = serverToken;
    }

    public string RegistryPath { get; }
    public string DefaultDatasetFolder { get; }
    public string? LastOpenedId { get; private set; }
    public bool DarkMode { get; private set; }
    public string? ServerUrl { get; private set; }
    public string? ServerToken { get; private set; }
    public IReadOnlyList<DatasetEntry> Datasets => _datasets;

    /// <summary>Folder holding local cache copies of server datasets.</summary>
    public string ServerCacheFolder => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(RegistryPath)!, "ServerCache");

    public string BuildServerCachePath(string serverId)
    {
        var folder = ServerCacheFolder;
        Directory.CreateDirectory(folder);
        return System.IO.Path.Combine(folder, $"{serverId}.padel");
    }

    public void SetServerConnection(string? url, string? token)
    {
        ServerUrl = string.IsNullOrWhiteSpace(url) ? null : url.Trim().TrimEnd('/');
        ServerToken = string.IsNullOrWhiteSpace(token) ? null : token;
    }

    public static DatasetRegistry Load()
    {
        var configDir = GetConfigDir();
        var registryPath = System.IO.Path.Combine(configDir, RegistryFileName);
        var defaultDatasetFolder = GetDefaultDatasetFolder();

        Directory.CreateDirectory(configDir);
        Directory.CreateDirectory(defaultDatasetFolder);

        List<DatasetEntry> datasets;
        string? lastOpenedId = null;
        bool darkMode = false;
        string? serverUrl = null;
        string? serverToken = null;

        if (File.Exists(registryPath))
        {
            try
            {
                var json = File.ReadAllText(registryPath);
                var stored = JsonSerializer.Deserialize<StoredRegistry>(json, JsonOptions);
                datasets = stored?.Datasets ?? new List<DatasetEntry>();
                lastOpenedId = stored?.LastOpenedId;
                darkMode = stored?.DarkMode ?? false;
                serverUrl = stored?.ServerUrl;
                serverToken = stored?.ServerToken;
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

        var registry = new DatasetRegistry(registryPath, defaultDatasetFolder, datasets, lastOpenedId, darkMode, serverUrl, serverToken);
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

    public void SetDarkMode(bool value) => DarkMode = value;

    public void Save()
    {
        var stored = new StoredRegistry
        {
            Datasets = _datasets,
            LastOpenedId = LastOpenedId,
            DarkMode = DarkMode,
            ServerUrl = ServerUrl,
            ServerToken = ServerToken
        };
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

        [JsonPropertyName("darkMode")]
        public bool DarkMode { get; set; }

        [JsonPropertyName("serverUrl")]
        public string? ServerUrl { get; set; }

        [JsonPropertyName("serverToken")]
        public string? ServerToken { get; set; }
    }
}

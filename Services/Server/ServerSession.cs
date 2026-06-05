using System.IO;
using Padel.Manager.Services.Server;

namespace Padel.Manager.Services;

/// <summary>
/// Connection-level server operations for the desktop app: configure URL, log in,
/// list/upload/download datasets, and make local backups. Persists the URL + token
/// into the <see cref="DatasetRegistry"/> so the connection survives restarts.
/// </summary>
public sealed class ServerSession
{
    private readonly DatasetRegistry _registry;
    private ServerClient? _client;

    public ServerSession(DatasetRegistry registry)
    {
        _registry = registry;
        if (!string.IsNullOrWhiteSpace(registry.ServerUrl))
        {
            _client = new ServerClient(registry.ServerUrl);
            _client.UseToken(registry.ServerToken);
        }
    }

    public bool IsConfigured => _client is not null;
    public bool HasToken => !string.IsNullOrEmpty(_client?.Token);
    public string? Url => _client?.BaseUrl;
    public ServerClient Client => _client ?? throw new InvalidOperationException("Le serveur n'est pas configuré.");

    /// <summary>Point the session at a server URL (does not log in).</summary>
    public void Configure(string url)
    {
        _client?.Dispose();
        _client = new ServerClient(url);
        _registry.SetServerConnection(_client.BaseUrl, null);
        _registry.Save();
    }

    public async Task LoginAsync(string username, string password, CancellationToken ct = default)
    {
        if (_client is null)
        {
            throw new InvalidOperationException("Configurez l'adresse du serveur avant de vous connecter.");
        }

        var token = await _client.LoginAsync(username, password, ct);
        _registry.SetServerConnection(_client.BaseUrl, token);
        _registry.Save();
    }

    /// <summary>Creates a new account on the server and logs in with it.</summary>
    public async Task RegisterAsync(string username, string password, CancellationToken ct = default)
    {
        if (_client is null)
        {
            throw new InvalidOperationException("Configurez l'adresse du serveur avant de créer un compte.");
        }

        var token = await _client.RegisterAsync(username, password, ct);
        _registry.SetServerConnection(_client.BaseUrl, token);
        _registry.Save();
    }

    public void Logout()
    {
        _client?.UseToken(null);
        _registry.SetServerConnection(Url, null);
        _registry.Save();
    }

    public Task<IReadOnlyList<ServerDatasetSummary>> ListAsync(CancellationToken ct = default)
        => Client.ListAsync(ct);

    /// <summary>Downloads a server dataset into its local cache file and registers/refreshes the entry.</summary>
    public async Task<DatasetEntry> OpenAsync(ServerDatasetSummary summary, CancellationToken ct = default)
    {
        var detail = await Client.GetAsync(summary.Id, ct);
        var cachePath = _registry.BuildServerCachePath(detail.Id);
        await File.WriteAllTextAsync(cachePath, detail.Json, ct);

        var entry = _registry.Datasets.FirstOrDefault(d => d.IsServer && d.ServerId == detail.Id)
                    ?? new DatasetEntry { Source = DatasetSource.Server, ServerId = detail.Id };
        entry.Name = detail.Name;
        entry.Path = cachePath;
        entry.ServerVersion = detail.Version;
        entry.LastOpenedUtc = DateTime.UtcNow;
        _registry.AddOrUpdate(entry);
        _registry.Save();
        return entry;
    }

    /// <summary>Re-downloads an already-known server entry into its cache (latest server copy).</summary>
    public async Task RefreshAsync(DatasetEntry entry, CancellationToken ct = default)
    {
        var detail = await Client.GetAsync(entry.ServerId!, ct);
        await File.WriteAllTextAsync(entry.Path, detail.Json, ct);
        entry.Name = detail.Name;
        entry.ServerVersion = detail.Version;
        _registry.AddOrUpdate(entry);
        _registry.Save();
    }

    public Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken ct = default)
        => Client.ChangePasswordAsync(currentPassword, newPassword, ct);

    /// <summary>Uploads an existing local dataset file to the server as a new server dataset.</summary>
    public async Task<DatasetEntry> UploadLocalFileAsync(DatasetEntry localEntry, CancellationToken ct = default)
    {
        var json = await File.ReadAllTextAsync(localEntry.Path, ct);
        return await UploadAsync(localEntry.Name, json, ct);
    }

    /// <summary>Uploads a local dataset's JSON as a new server dataset and returns the new server entry.</summary>
    public async Task<DatasetEntry> UploadAsync(string name, string json, CancellationToken ct = default)
    {
        var (id, version) = await Client.CreateAsync(name, json, ct);
        var cachePath = _registry.BuildServerCachePath(id);
        await File.WriteAllTextAsync(cachePath, json, ct);

        var entry = new DatasetEntry
        {
            Source = DatasetSource.Server,
            ServerId = id,
            Name = name,
            Path = cachePath,
            ServerVersion = version,
            LastOpenedUtc = DateTime.UtcNow
        };
        _registry.AddOrUpdate(entry);
        _registry.Save();
        return entry;
    }

    /// <summary>
    /// Copies a server dataset's current local cache into a permanent local <c>.padel</c>
    /// file and registers it as an ordinary local dataset — openable with no server.
    /// </summary>
    public DatasetEntry SaveLocalBackup(DatasetEntry serverEntry, string? backupName = null)
    {
        var name = string.IsNullOrWhiteSpace(backupName)
            ? $"{serverEntry.Name} (sauvegarde {DateTime.Now:yyyy-MM-dd})"
            : backupName!;
        var path = _registry.BuildNewDatasetPath(name);
        File.Copy(serverEntry.Path, path, overwrite: false);

        var entry = new DatasetEntry
        {
            Source = DatasetSource.Local,
            Name = name,
            Path = path,
            LastOpenedUtc = DateTime.UtcNow
        };
        _registry.AddOrUpdate(entry);
        _registry.Save();
        return entry;
    }

    public ServerSyncBinding CreateSyncBinding(DatasetEntry entry, WorkbookService workbook)
        => new(Client, _registry, entry, workbook);
}

using System.IO;
using Padel.Manager.Services.Server;

namespace Padel.Manager.Services;

/// <summary>
/// While a server dataset is open, pushes every saved change up to the server
/// (debounced). Raises events for status, conflicts (HTTP 409), and offline
/// errors so the UI can react. Auto-push pauses after a conflict until
/// <see cref="Resume"/> is called.
/// </summary>
public sealed class ServerSyncBinding : IDisposable
{
    private const int DebounceMs = 800;

    private readonly ServerClient _client;
    private readonly DatasetRegistry _registry;
    private readonly DatasetEntry _entry;
    private readonly WorkbookService _workbook;
    private readonly SemaphoreSlim _pushLock = new(1, 1);

    private CancellationTokenSource? _debounce;
    private bool _paused;
    private bool _disposed;

    public ServerSyncBinding(ServerClient client, DatasetRegistry registry, DatasetEntry entry, WorkbookService workbook)
    {
        _client = client;
        _registry = registry;
        _entry = entry;
        _workbook = workbook;
        _workbook.Changed += OnWorkbookChanged;
    }

    /// <summary>Human-readable sync status (may fire on a background thread).</summary>
    public event Action<string>? Status;

    /// <summary>The server copy changed underneath us; auto-push is now paused.</summary>
    public event Action<ServerConflictException>? Conflict;

    /// <summary>The server was unreachable; the change stays safe in the local cache.</summary>
    public event Action<ServerUnavailableException>? Offline;

    public event Action<int>? Pushed;

    public DatasetEntry Entry => _entry;

    private void OnWorkbookChanged()
    {
        if (_paused || _disposed)
        {
            return;
        }

        _debounce?.Cancel();
        var cts = new CancellationTokenSource();
        _debounce = cts;
        _ = DebouncedPushAsync(cts.Token);
    }

    private async Task DebouncedPushAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(DebounceMs, ct);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        await PushAsync(ct);
    }

    /// <summary>Pushes the current cache file to the server immediately.</summary>
    public async Task PushAsync(CancellationToken ct = default)
    {
        await _pushLock.WaitAsync(ct);
        try
        {
            var json = await File.ReadAllTextAsync(_entry.Path, ct);
            Status?.Invoke("Synchronisation…");
            var newVersion = await _client.UpdateAsync(_entry.ServerId!, _entry.Name, _entry.ServerVersion, json, ct);
            _entry.ServerVersion = newVersion;
            _registry.AddOrUpdate(_entry);
            _registry.Save();
            Pushed?.Invoke(newVersion);
            Status?.Invoke($"Enregistré sur le serveur à {DateTime.Now:HH:mm:ss}");
        }
        catch (ServerConflictException ex)
        {
            _paused = true;
            Conflict?.Invoke(ex);
        }
        catch (ServerUnavailableException ex)
        {
            // Transient: keep auto-push armed so the next change retries.
            Offline?.Invoke(ex);
        }
        catch (Exception ex)
        {
            // Unexpected (e.g. dataset no longer owned by this account / deleted on the
            // server). Stop auto-pushing so we don't loop; the change stays in the cache.
            _paused = true;
            Offline?.Invoke(new ServerUnavailableException(
                "Synchronisation impossible avec le serveur : " + ex.Message, ex));
        }
        finally
        {
            _pushLock.Release();
        }
    }

    /// <summary>Force the server copy to match our local version after a conflict, then resume.</summary>
    public async Task OverwriteServerAsync(int serverVersion, CancellationToken ct = default)
    {
        _entry.ServerVersion = serverVersion;
        _registry.AddOrUpdate(_entry);
        _registry.Save();
        _paused = false;
        await PushAsync(ct);
    }

    public void Resume() => _paused = false;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _workbook.Changed -= OnWorkbookChanged;
        _debounce?.Cancel();
    }
}

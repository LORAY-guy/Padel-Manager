using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Padel.Manager.Services.Server;

/// <summary>
/// Typed client for the Padel server API. Stateless beyond the bearer token; one
/// instance can be reused for the lifetime of a server connection.
/// </summary>
public sealed class ServerClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public ServerClient(string baseUrl)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient { BaseAddress = new Uri(BaseUrl + "/"), Timeout = TimeSpan.FromSeconds(15) };
    }

    public string BaseUrl { get; }

    public string? Token { get; private set; }

    public void UseToken(string? token)
    {
        Token = token;
        _http.DefaultRequestHeaders.Authorization = string.IsNullOrEmpty(token)
            ? null
            : new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>Authenticates and stores the returned token on this client.</summary>
    public async Task<string> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync("api/login", new { username, password }, JsonOptions, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ServerUnavailableException($"Serveur injoignable ({BaseUrl}).", ex);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new ServerAuthException("Identifiant ou mot de passe incorrect.");
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions, ct)
                   ?? throw new ServerUnavailableException("Réponse de connexion invalide.");
        UseToken(body.Token);
        return body.Token;
    }

    /// <summary>Creates a new account and logs in (stores the returned token on this client).</summary>
    public async Task<string> RegisterAsync(string username, string password, CancellationToken ct = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync("api/register", new { username, password }, JsonOptions, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ServerUnavailableException($"Serveur injoignable ({BaseUrl}).", ex);
        }

        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest)
        {
            var error = await response.Content.ReadFromJsonAsync<MessageResponse>(JsonOptions, ct);
            throw new InvalidOperationException(error?.Message ?? "Inscription refusée.");
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions, ct)
                   ?? throw new ServerUnavailableException("Réponse d'inscription invalide.");
        UseToken(body.Token);
        return body.Token;
    }

    public async Task<IReadOnlyList<ServerDatasetSummary>> ListAsync(CancellationToken ct = default)
    {
        var response = await SendAsync(() => _http.GetAsync("api/datasets", ct), ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<ServerDatasetSummary>>(JsonOptions, ct)
               ?? new List<ServerDatasetSummary>();
    }

    public async Task<ServerDatasetDetail> GetAsync(string id, CancellationToken ct = default)
    {
        var response = await SendAsync(() => _http.GetAsync($"api/datasets/{id}", ct), ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ServerDatasetDetail>(JsonOptions, ct)
               ?? throw new ServerUnavailableException("Réponse serveur invalide.");
    }

    public async Task<(string Id, int Version)> CreateAsync(string name, string json, CancellationToken ct = default)
    {
        var response = await SendAsync(
            () => _http.PostAsJsonAsync("api/datasets", new { name, json }, JsonOptions, ct), ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CreateResponse>(JsonOptions, ct)
                   ?? throw new ServerUnavailableException("Réponse serveur invalide.");
        return (body.Id, body.Version);
    }

    /// <summary>Saves a dataset. Throws <see cref="ServerConflictException"/> on a stale version (409).</summary>
    public async Task<int> UpdateAsync(string id, string name, int version, string json, CancellationToken ct = default)
    {
        var response = await SendAsync(
            () => _http.PutAsJsonAsync($"api/datasets/{id}", new { name, version, json }, JsonOptions, ct), ct);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var conflict = await response.Content.ReadFromJsonAsync<ConflictResponse>(JsonOptions, ct);
            throw new ServerConflictException(conflict?.ServerVersion ?? version);
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<SaveResponse>(JsonOptions, ct)
                   ?? throw new ServerUnavailableException("Réponse serveur invalide.");
        return body.Version;
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var response = await SendAsync(() => _http.DeleteAsync($"api/datasets/{id}", ct), ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Changes the logged-in account's password. Throws on a wrong current password.</summary>
    public async Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var response = await SendAsync(
            () => _http.PostAsJsonAsync("api/change-password",
                new { currentPassword, newPassword }, JsonOptions, ct), ct);

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var body = await response.Content.ReadFromJsonAsync<MessageResponse>(JsonOptions, ct);
            throw new InvalidOperationException(body?.Message ?? "Changement de mot de passe refusé.");
        }

        response.EnsureSuccessStatusCode();
    }

    private static async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        try
        {
            return await send();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new ServerUnavailableException("Le serveur est injoignable.", ex);
        }
    }

    public void Dispose() => _http.Dispose();

    private sealed record LoginResponse(string Token);
    private sealed record CreateResponse(string Id, int Version);
    private sealed record SaveResponse(int Version);
    private sealed record ConflictResponse(string? Message, int ServerVersion);
    private sealed record MessageResponse(string? Message);
}

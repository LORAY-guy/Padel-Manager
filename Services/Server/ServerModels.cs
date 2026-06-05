namespace Padel.Manager.Services.Server;

/// <summary>Server dataset listing entry (no payload).</summary>
public sealed record ServerDatasetSummary(string Id, string Name, int Version, DateTime UpdatedUtc);

/// <summary>Full server dataset including the serialized PadelDataFile JSON.</summary>
public sealed record ServerDatasetDetail(string Id, string Name, int Version, string Json);

/// <summary>Thrown when a save is rejected because the dataset changed on the server (HTTP 409).</summary>
public sealed class ServerConflictException : Exception
{
    public ServerConflictException(int serverVersion)
        : base("Le dataset a été modifié sur le serveur depuis son ouverture.")
        => ServerVersion = serverVersion;

    public int ServerVersion { get; }
}

/// <summary>Thrown for connectivity / server-unreachable problems (distinct from auth or conflict).</summary>
public sealed class ServerUnavailableException : Exception
{
    public ServerUnavailableException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>Thrown when credentials are rejected (HTTP 401 on login).</summary>
public sealed class ServerAuthException : Exception
{
    public ServerAuthException(string message) : base(message)
    {
    }
}

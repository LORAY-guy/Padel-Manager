namespace Padel.Server.Contracts;

public sealed record LoginRequest(string Username, string Password);

public sealed record RegisterRequest(string Username, string Password, string? InviteCode = null);

public sealed record LoginResponse(string Token);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>Lightweight listing — no JSON payload.</summary>
public sealed record DatasetSummary(string Id, string Name, int Version, DateTime UpdatedUtc);

/// <summary>Full dataset including the serialized PadelDataFile JSON.</summary>
public sealed record DatasetDetail(string Id, string Name, int Version, string Json);

public sealed record CreateDatasetRequest(string Name, string Json);

public sealed record CreateDatasetResponse(string Id, int Version);

/// <summary>
/// Save an existing dataset. <see cref="Version"/> is the version the client last
/// loaded; if it no longer matches the server, the save is rejected with 409.
/// </summary>
public sealed record SaveDatasetRequest(string Name, int Version, string Json);

public sealed record SaveDatasetResponse(int Version);

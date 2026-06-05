namespace Padel.Server.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Signing key. Must be overridden in production (env var / user-secrets).</summary>
    public string Key { get; set; } = string.Empty;

    public string Issuer { get; set; } = "PadelManager";

    public string Audience { get; set; } = "PadelManager";

    public int ExpiryDays { get; set; } = 30;
}

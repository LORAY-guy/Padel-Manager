namespace Padel.Server.Auth;

/// <summary>
/// Controls who may create an account. Default is open (fine on a LAN); switch to
/// invite or closed before exposing the server to the internet.
/// </summary>
public sealed class RegistrationOptions
{
    public const string SectionName = "Registration";

    /// <summary>"open" (anyone), "invite" (requires <see cref="InviteCode"/>), or "closed".</summary>
    public string Mode { get; set; } = "open";

    public string? InviteCode { get; set; }

    public bool IsOpen => string.Equals(Mode, "open", StringComparison.OrdinalIgnoreCase);
    public bool IsClosed => string.Equals(Mode, "closed", StringComparison.OrdinalIgnoreCase);
    public bool IsInvite => string.Equals(Mode, "invite", StringComparison.OrdinalIgnoreCase);

    /// <summary>True if registration may proceed given the supplied invite code (if any).</summary>
    public bool Allows(string? suppliedCode)
    {
        if (IsClosed)
        {
            return false;
        }

        if (IsInvite)
        {
            return !string.IsNullOrEmpty(InviteCode)
                   && string.Equals(suppliedCode?.Trim(), InviteCode, StringComparison.Ordinal);
        }

        return true; // open
    }
}

using System.ComponentModel.DataAnnotations;

namespace Padel.Server.Data;

/// <summary>
/// A login account. For now there is a single shared "group" account, but the
/// table supports more (and a Role column) so admin/viewer logins can be added
/// later without a schema change.
/// </summary>
public sealed class AccountRecord
{
    [Key]
    public string Username { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string Role { get; set; } = "group";
}

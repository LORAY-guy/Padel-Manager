using System.ComponentModel.DataAnnotations;

namespace Padel.Server.Data;

/// <summary>
/// A stored dataset. <see cref="Json"/> is the serialized PadelDataFile — the
/// exact same payload local <c>.padel</c> files hold, stored opaquely as text.
/// </summary>
public sealed class DatasetRecord
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Username of the account that owns this dataset; data is private per user.</summary>
    public string Owner { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Json { get; set; } = string.Empty;

    /// <summary>Bumped on every save; used for optimistic-concurrency checks.</summary>
    public int Version { get; set; } = 1;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

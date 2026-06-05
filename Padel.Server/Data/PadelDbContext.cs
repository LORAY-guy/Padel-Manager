using Microsoft.EntityFrameworkCore;

namespace Padel.Server.Data;

public sealed class PadelDbContext : DbContext
{
    public PadelDbContext(DbContextOptions<PadelDbContext> options) : base(options)
    {
    }

    public DbSet<DatasetRecord> Datasets => Set<DatasetRecord>();

    public DbSet<AccountRecord> Accounts => Set<AccountRecord>();
}

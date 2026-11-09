using Microsoft.EntityFrameworkCore;

namespace Quests.Api.Infrastructure.Data;

/// <summary>Application database (SQLite). Entity configurations are picked up from this assembly.</summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}

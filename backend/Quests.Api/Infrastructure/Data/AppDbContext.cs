using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Quests.Api.Features.Identity;
using Quests.Api.Features.Tasks;

namespace Quests.Api.Infrastructure.Data;

/// <summary>
/// Application database (SQLite). Entity configurations are picked up from this assembly.
/// Every <see cref="IOwnedEntity"/> is filtered by the current user's id (fail closed when there is none).
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options, CurrentUser currentUser) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    /// <summary>Owner id used by the query filter; EF Core re-evaluates it per context instance.</summary>
    private Guid? CurrentOwnerId => currentUser.Id;

    private static readonly PropertyInfo CurrentOwnerIdProperty =
        typeof(AppDbContext).GetProperty(nameof(CurrentOwnerId), BindingFlags.Instance | BindingFlags.NonPublic)!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        ApplyOwnerFilters(modelBuilder);
    }

    private void ApplyOwnerFilters(ModelBuilder modelBuilder)
    {
        var ownedTypes = modelBuilder.Model.GetEntityTypes()
            .Where(t => t.BaseType is null && typeof(IOwnedEntity).IsAssignableFrom(t.ClrType));

        foreach (var entityType in ownedTypes)
        {
            // e => (Guid?)e.OwnerId == this.CurrentOwnerId
            var entity = Expression.Parameter(entityType.ClrType, "e");
            var ownerId = Expression.Convert(
                Expression.Property(entity, nameof(IOwnedEntity.OwnerId)), typeof(Guid?));
            var currentOwnerId = Expression.Property(Expression.Constant(this), CurrentOwnerIdProperty);
            var filter = Expression.Lambda(Expression.Equal(ownerId, currentOwnerId), entity);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
        }
    }
}

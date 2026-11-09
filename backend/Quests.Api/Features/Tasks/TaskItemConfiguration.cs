using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quests.Api.Features.Identity;

namespace Quests.Api.Features.Tasks;

public sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.ToTable("Tasks");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Title).IsRequired().HasMaxLength(TaskItem.TitleMaxLength);
        builder.Property(t => t.Notes).HasMaxLength(TaskItem.NotesMaxLength);
        builder.Property(t => t.CreatedAt).IsRequired();
        builder.Property(t => t.UpdatedAt).IsRequired();
        builder.Ignore(t => t.IsCompleted);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(t => t.OwnerId);
    }
}

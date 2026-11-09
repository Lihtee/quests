using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Quests.Api.Features.Identity;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.FirebaseUid).IsRequired().HasMaxLength(128);
        builder.Property(u => u.Email).IsRequired().HasMaxLength(320);
        builder.Property(u => u.CreatedAt).IsRequired();
        builder.HasIndex(u => u.FirebaseUid).IsUnique();
    }
}

using FutureFlags.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FutureFlags.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(user => user.Id);

        // Ids are minted by Better Auth as UUIDv7 strings and cast to uuid by the mirror trigger.
        builder.Property(user => user.Id)
            .ValueGeneratedNever();

        builder.Property(user => user.Email)
            .HasMaxLength(User.MaxEmailLength)
            .IsRequired();

        builder.HasIndex(user => user.Email)
            .IsUnique();

        builder.Property(user => user.Name)
            .HasMaxLength(User.MaxNameLength)
            .IsRequired();

        builder.Property(user => user.Role)
            .HasConversion(
                role => role.Value,
                value => UserRole.FromPersisted(value))
            .HasMaxLength(UserRole.MaxLength)
            .IsRequired();

        builder.Property(user => user.CreatedAt)
            .IsRequired();

        builder.Property(user => user.UpdatedAt)
            .IsRequired();
    }
}

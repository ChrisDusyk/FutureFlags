using FutureFlags.Domain.Environments;
using FutureFlags.Domain.SdkKeys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FutureFlags.Infrastructure.Persistence.Configurations;

internal sealed class SdkKeyConfiguration : IEntityTypeConfiguration<SdkKey>
{
    /// <summary>
    /// Named explicitly rather than left to convention because the selector index is what the
    /// authentication path depends on: every request presenting an SDK key is one lookup through it.
    /// </summary>
    internal const string SelectorIndexName = "IX_sdk_keys_Selector";

    public void Configure(EntityTypeBuilder<SdkKey> builder)
    {
        builder.ToTable("sdk_keys");

        builder.HasKey(key => key.Id);

        // Ids come from Guid.CreateVersion7() in the domain factory, not from the database.
        builder.Property(key => key.Id)
            .ValueGeneratedNever();

        builder.Property(key => key.Name)
            .HasMaxLength(SdkKey.MaxNameLength)
            .IsRequired();

        builder.Property(key => key.Kind)
            .HasConversion(
                kind => kind.Value,
                value => SdkKeyKind.FromPersisted(value))
            .HasMaxLength(SdkKeyKind.MaxLength)
            .IsRequired();

        builder.Property(key => key.Environment)
            .HasConversion(
                environment => environment.Value,
                value => EnvironmentKey.FromPersisted(value))
            .HasMaxLength(EnvironmentKey.MaxLength)
            .IsRequired();

        builder.Property(key => key.Selector)
            .HasMaxLength(SdkKeyToken.SelectorLength)
            .IsRequired();

        builder.HasIndex(key => key.Selector)
            .HasDatabaseName(SelectorIndexName)
            .IsUnique();

        // Private on the entity so nothing outside it can read the hash back out — SdkKey.Matches is
        // the only comparison there is. EF reaches it by name.
        builder.Property<byte[]>("SecretHash")
            .IsRequired();

        builder.Property(key => key.CreatedBy)
            .IsRequired();

        builder.Property(key => key.CreatedAt)
            .IsRequired();

        // Backing fields rather than properties: both are Option<DateTimeOffset> to the domain, and
        // an Option is a modelling type rather than a storage one. Null in the column is the None.
        builder.Property<DateTimeOffset?>("_lastUsedAt")
            .HasColumnName("LastUsedAt");

        builder.Property<DateTimeOffset?>("_revokedAt")
            .HasColumnName("RevokedAt");
    }
}

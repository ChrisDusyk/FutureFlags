using FeatureFlags.Domain.Segments;
using FeatureFlags.Infrastructure.Persistence.Events;
using FeatureFlags.Infrastructure.Persistence.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FeatureFlags.Infrastructure.Persistence.Configurations;

internal sealed class SegmentRowConfiguration : IEntityTypeConfiguration<SegmentRow>
{
    /// <summary>Named explicitly because <see cref="Repositories.SegmentRepository"/> matches on it
    /// to turn a Postgres unique-violation into a conflict a caller can act on.</summary>
    internal const string KeyIndexName = "IX_segments_Key";

    public void Configure(EntityTypeBuilder<SegmentRow> builder)
    {
        builder.ToTable("segments");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();

        builder.Property(row => row.Key)
            .HasConversion(key => key.Value, value => SegmentKey.FromPersisted(value))
            .HasMaxLength(SegmentKey.MaxLength)
            .IsRequired();

        // Unique across live and retired rows alike. That is the whole mechanism behind not
        // reissuing a deleted segment's key: the index refuses it whatever the tombstone says.
        builder.HasIndex(row => row.Key).HasDatabaseName(KeyIndexName).IsUnique();

        builder.Property(row => row.Name).HasMaxLength(Segment.MaxNameLength).IsRequired();
        builder.Property(row => row.Description).HasMaxLength(Segment.MaxDescriptionLength).IsRequired();

        builder.Property(row => row.Definition)
            .HasColumnType("jsonb")
            .HasConversion(
                definition => SegmentEventSerializer.SerializeDefinition(definition),
                json => SegmentEventSerializer.DeserializeDefinition(json))
            // A converted reference type needs a comparer, or EF compares snapshots by reference and
            // never notices that a definition changed. SegmentDefinition is immutable and has real
            // value equality, so the snapshot can be the instance itself.
            .Metadata.SetValueComparer(new ValueComparer<SegmentDefinition>(
                (left, right) => left == right,
                definition => definition.GetHashCode(),
                definition => definition));

        builder.Property(row => row.CreatedAt).IsRequired();
        builder.Property(row => row.UpdatedAt).IsRequired();
        builder.Property(row => row.DeletedAt).IsRequired(false);
    }
}

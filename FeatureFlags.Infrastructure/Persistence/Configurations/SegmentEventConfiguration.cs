using FeatureFlags.Infrastructure.Persistence.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FeatureFlags.Infrastructure.Persistence.Configurations;

internal sealed class SegmentEventConfiguration : IEntityTypeConfiguration<SegmentEventRecord>
{
    public void Configure(EntityTypeBuilder<SegmentEventRecord> builder)
    {
        builder.ToTable("segment_events");

        // Composite key, doubling as the concurrency guard, exactly as flag_events has it: a second
        // writer computing the same next sequence number for the same segment collides on this key.
        builder.HasKey(record => new { record.SegmentId, record.SequenceNumber });

        builder.Property(record => record.EventType)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(record => record.Payload)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(record => record.OccurredAt)
            .IsRequired();

        builder.Property(record => record.CausedBy)
            .IsRequired(false);

        // Deliberately no FK to segments: this stream is the source of truth, so the dependency
        // runs the other way. It is also what lets a retired segment's history stay readable.
    }
}

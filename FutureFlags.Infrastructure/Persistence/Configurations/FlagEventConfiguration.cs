using FutureFlags.Infrastructure.Persistence.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FutureFlags.Infrastructure.Persistence.Configurations;

internal sealed class FlagEventConfiguration : IEntityTypeConfiguration<FlagEventRecord>
{
    public void Configure(EntityTypeBuilder<FlagEventRecord> builder)
    {
        builder.ToTable("flag_events");

        // Composite key, not a synthetic id — the same "let the database carry the constraint"
        // choice FlagState already makes. It doubles as the concurrency guard: a second writer
        // computing the same next sequence number for the same flag collides on this key.
        builder.HasKey(record => new { record.FlagId, record.SequenceNumber });

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

        // Deliberately no FK to feature_flags: flag_events is the source of truth here, so the
        // dependency runs the other way — the read-model row is what depends on this stream, not
        // the reverse.
    }
}

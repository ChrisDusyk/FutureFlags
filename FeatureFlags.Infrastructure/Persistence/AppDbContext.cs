using FeatureFlags.Domain.SdkKeys;
using FeatureFlags.Domain.Users;
using FeatureFlags.Infrastructure.Persistence.Events;
using FeatureFlags.Infrastructure.Persistence.ReadModels;
using Microsoft.EntityFrameworkCore;

namespace FeatureFlags.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    /// <summary>The source of truth for a flag's state — append-only, ordered per flag by
    /// <see cref="FlagEventRecord.SequenceNumber"/>.</summary>
    internal DbSet<FlagEventRecord> FlagEvents => Set<FlagEventRecord>();

    /// <summary>The projected current state of every flag, derived from <see cref="FlagEvents"/>.
    /// Fast to read; never written to directly outside <c>FeatureFlagRepository</c>.</summary>
    internal DbSet<FlagRow> FlagRows => Set<FlagRow>();

    /// <summary>The source of truth for a segment's definition — append-only, ordered per segment
    /// by <see cref="SegmentEventRecord.SequenceNumber"/>.</summary>
    internal DbSet<SegmentEventRecord> SegmentEvents => Set<SegmentEventRecord>();

    /// <summary>The projected current state of every segment, derived from
    /// <see cref="SegmentEvents"/>. Carries a tombstone rather than being deleted from, so that a
    /// retired segment's stream stays reachable and its key is never reissued.</summary>
    internal DbSet<SegmentRow> SegmentRows => Set<SegmentRow>();

    /// <summary>
    /// The credentials programs authenticate with. Owned entirely here, unlike <see cref="Users"/> —
    /// Better Auth issues identities, this application issues machine keys.
    /// </summary>
    public DbSet<SdkKey> SdkKeys => Set<SdkKey>();

    /// <summary>
    /// A read-only mirror of <c>auth.user</c>, maintained by a trigger rather than by EF.
    /// Tracking changes here would be misleading — nothing in this application writes it.
    /// </summary>
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}

using FutureFlags.Domain.Environments;
using FutureFlags.Domain.Segments;
using FutureFlags.Evaluation;

namespace FutureFlags.Domain.Flags;

/// <summary>
/// Whether a flag is on in one environment. Owned by its <see cref="FeatureFlag"/> — a state has no
/// meaning apart from the flag it belongs to, which is why only the flag can make or change one.
/// </summary>
public sealed class FlagState
{
    internal FlagState(
        EnvironmentKey environment,
        bool isEnabled,
        IReadOnlyList<SegmentKey> targetedSegments,
        DateTimeOffset updatedAt,
        string onVariant = FlagVariantNames.On,
        string offVariant = FlagVariantNames.Off)
    {
        Environment = environment;
        IsEnabled = isEnabled;
        TargetedSegments = targetedSegments;
        UpdatedAt = updatedAt;
        OnVariant = onVariant;
        OffVariant = offVariant;
    }

    public EnvironmentKey Environment { get; private set; }
    public bool IsEnabled { get; private set; }

    /// <summary>
    /// The variant served here when the flag reaches a context, and the one served when it does
    /// not — off in this environment, or targeted at segments the context is not in.
    ///
    /// <para>
    /// Per environment rather than on the flag, because that is the axis a flag's state already
    /// varies on. Always <c>on</c> and <c>off</c> while every flag is boolean; they exist now so a
    /// typed flag later is a domain change rather than another event-stream change.
    /// </para>
    /// </summary>
    public string OnVariant { get; private set; }

    /// <inheritdoc cref="OnVariant"/>
    public string OffVariant { get; private set; }

    /// <summary>
    /// Which segments this flag reaches here, deduplicated and ordinal-sorted.
    ///
    /// <para>
    /// Empty means everyone, which is what a flag meant before segments existed — so a flag nobody
    /// has targeted keeps answering exactly as it always did. It is emphatically not "nobody": the
    /// other reading would turn every existing flag off the moment this shipped.
    /// </para>
    /// </summary>
    public IReadOnlyList<SegmentKey> TargetedSegments { get; private set; }

    /// <summary>
    /// When this environment last changed. Deliberately separate from the flag's own
    /// <see cref="FeatureFlag.UpdatedAt"/>: turning a flag on in production says nothing about
    /// development, and a list scoped to one environment should not report otherwise.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Folds a <c>FlagStateChanged</c> fact into this state. Unconditional — whether this is worth
    /// raising at all is a decision <see cref="FeatureFlag.SetEnabled"/> makes before an event ever
    /// reaches here, not something this method judges.
    /// </summary>
    internal void Apply(bool isEnabled, string onVariant, string offVariant, DateTimeOffset updatedAt)
    {
        IsEnabled = isEnabled;
        OnVariant = onVariant;
        OffVariant = offVariant;
        UpdatedAt = updatedAt;
    }

    /// <summary>
    /// Folds a <c>FlagTargetingChanged</c> fact into this state. Unconditional, on the same terms as
    /// <see cref="Apply(bool, string, string, DateTimeOffset)"/> — whether it was worth raising is a decision
    /// <see cref="FeatureFlag.SetTargeting"/> already made.
    /// </summary>
    internal void ApplyTargeting(IReadOnlyList<SegmentKey> targetedSegments, DateTimeOffset updatedAt)
    {
        TargetedSegments = targetedSegments;
        UpdatedAt = updatedAt;
    }
}

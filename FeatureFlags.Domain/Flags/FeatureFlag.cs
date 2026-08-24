using FeatureFlags.Domain.Environments;
using FeatureFlags.Domain.Flags.Events;
using FeatureFlags.Domain.Segments;
using FeatureFlags.Domain.Shared;

namespace FeatureFlags.Domain.Flags;

/// <summary>
/// A single toggleable feature, identified by its <see cref="FlagKey"/>.
/// <para>
/// A flag's identity is global — one key, one name, one description, everywhere. Whether it is
/// <em>on</em> is answered once per environment by a <see cref="FlagState"/>, so a feature can be
/// live in development and dark in production while still being the same flag.
/// </para>
/// <para>
/// State lives nowhere but the events in <see cref="UncommittedEvents"/> and whatever a prior
/// <see cref="Rehydrate"/> folded in — <see cref="Create"/> and <see cref="SetEnabled"/> are the
/// only two ways a flag's facts change, and both change them by raising an event rather than by
/// setting a field directly.
/// </para>
/// Timestamps are supplied by the caller rather than read from a clock so the entity stays deterministic.
/// </summary>
public sealed class FeatureFlag
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 1000;

    /// <summary>
    /// How many segments one environment's state may target. A ceiling rather than a considered
    /// limit — targeting is an OR, so a flag needing dozens of groups is describing one group
    /// nobody has named yet, and an unbounded list is a ruleset payload with no bound either.
    /// </summary>
    public const int MaxTargetedSegments = 50;

    private readonly List<FlagState> _states = [];
    private readonly List<IFlagEvent> _uncommittedEvents = [];

    // IDE0032 suggests an auto property here, which would leave a settable int for EF to pick up
    // by convention if this type is ever queried again — see the Version property below.
#pragma warning disable IDE0032
    private int _version;
#pragma warning restore IDE0032

    private FeatureFlag(Guid id)
    {
        Id = id;
        Key = null!;
        Name = null!;
        Description = null!;
    }

    public Guid Id { get; private set; }
    public FlagKey Key { get; private set; }
    public string Name { get; private set; }
    public string Description { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// When the flag itself last changed — its name or description. Toggling an environment moves
    /// that <see cref="FlagState.UpdatedAt"/>, not this one.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>One state per environment in <see cref="EnvironmentKey.All"/>, always.</summary>
    public IReadOnlyCollection<FlagState> States => _states;

    /// <summary>How many events have been applied to this instance, fresh or replayed — its place
    /// in its own stream. Getter-only and backed by a field, not an auto-property, so EF's
    /// by-convention mapping has no settable scalar to pick up if this type is ever queried again.</summary>
    public int Version => _version;

    /// <summary>
    /// Events raised since this instance was created or rehydrated, not yet appended. Wrapped
    /// rather than returning <c>_uncommittedEvents</c> itself, so a caller cannot cast back to
    /// <see cref="List{T}"/> and mutate or clear it out from under the aggregate.
    /// </summary>
    public IReadOnlyList<IFlagEvent> UncommittedEvents => _uncommittedEvents.AsReadOnly();

    /// <summary>
    /// Creates a flag in every environment at once. It is on only where <paramref name="enabledIn"/>
    /// says so and off everywhere else — a new flag reaching production unasked is not a default
    /// worth having.
    /// </summary>
    public static Result<FeatureFlag> Create(
        string? key,
        string? name,
        string? description,
        IEnumerable<EnvironmentKey> enabledIn,
        DateTimeOffset timestamp,
        Guid causedBy)
    {
        var keyResult = FlagKey.Create(key);
        if (keyResult.IsFailure)
            return Result.Failure<FeatureFlag>(keyResult.Error);

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<FeatureFlag>(FlagErrors.NameRequired);

        var trimmedName = name.Trim();
        if (trimmedName.Length > MaxNameLength)
            return Result.Failure<FeatureFlag>(FlagErrors.NameTooLong);

        var trimmedDescription = description?.Trim() ?? string.Empty;
        if (trimmedDescription.Length > MaxDescriptionLength)
            return Result.Failure<FeatureFlag>(FlagErrors.DescriptionTooLong);

        var enabled = enabledIn.ToHashSet();

        var flag = new FeatureFlag(Guid.CreateVersion7());
        flag.Raise(new FlagCreatedEvent(flag.Id, keyResult.Value, trimmedName, trimmedDescription, timestamp, causedBy));

        foreach (var environment in EnvironmentKey.All)
            flag.Raise(new FlagStateChangedEvent(flag.Id, environment, enabled.Contains(environment), timestamp, causedBy));

        return Result.Success(flag);
    }

    /// <summary>
    /// Rebuilds a flag by folding its full event history in order — no business validation, since
    /// every event already happened. The result carries no uncommitted events: replaying history is
    /// not the same as making it.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The stream belongs to a different flag, or its first event is not a <see cref="FlagCreatedEvent"/>.
    /// Both mean the repository handed this a corrupted or mismatched stream — a store bug, not
    /// something a caller can recover from, so this fails loudly rather than returning a flag with
    /// null required fields.
    /// </exception>
    public static FeatureFlag Rehydrate(Guid id, IEnumerable<IFlagEvent> events)
    {
        var flag = new FeatureFlag(id);

        foreach (var @event in events)
        {
            if (@event.FlagId != id)
                throw new InvalidOperationException(
                    $"Cannot rehydrate flag {id}: encountered an event for flag {@event.FlagId}.");

            flag.Apply(@event);
        }

        if (flag.Key is null)
            throw new InvalidOperationException($"Cannot rehydrate flag {id}: its stream contains no FlagCreatedEvent.");

        return flag;
    }

    public bool IsEnabledIn(EnvironmentKey environment) =>
        StateIn(environment).Match(state => state.IsEnabled, () => false);

    /// <summary>
    /// The state for one environment. <see cref="Option{T}.None"/> only when a flag predates an
    /// environment that was added after it — which cannot happen while the set is fixed, but the
    /// caller still has to answer for it rather than being handed a null.
    /// </summary>
    public Option<FlagState> StateIn(EnvironmentKey environment) =>
        _states.FirstOrDefault(state => state.Environment == environment).ToOption();

    /// <summary>
    /// Replaces the set of segments this flag reaches in one environment.
    ///
    /// <para>
    /// Idempotent like <see cref="SetEnabled"/>, but over a set rather than a bool: the argument is
    /// deduplicated and ordered before it is compared, so resubmitting the same segments in a
    /// different order raises nothing. Order carries no meaning — targeting is an OR — so there is
    /// nothing lost by normalising it, and a stable order is what keeps a ruleset ETag still.
    /// </para>
    /// <para>
    /// Whether these segments exist is not a question this aggregate can answer; it holds no
    /// repository. The handler checks, and every evaluation engine treats an unknown segment as a
    /// non-match, because a segment can always be retired between the check and the read.
    /// </para>
    /// </summary>
    public Result SetTargeting(
        EnvironmentKey environment,
        IEnumerable<SegmentKey> segments,
        DateTimeOffset timestamp,
        Guid causedBy)
    {
        var normalized = (segments ?? [])
            .Where(segment => segment is not null)
            .Distinct()
            .OrderBy(segment => segment.Value, StringComparer.Ordinal)
            .ToList();

        if (normalized.Count > MaxTargetedSegments)
            return Result.Failure(FlagErrors.TooManyTargetedSegments(Key));

        return StateIn(environment).Match(
            state =>
            {
                if (!state.TargetedSegments.SequenceEqual(normalized))
                    Raise(new FlagTargetingChangedEvent(Id, environment, normalized, timestamp, causedBy));

                return Result.Success();
            },
            () => Result.Failure(FlagErrors.StateMissing(Key, environment)));
    }

    /// <summary>
    /// Turns the flag on or off in one environment. Idempotent — setting the state it is already in
    /// raises no event and leaves both the state and its timestamp untouched.
    /// </summary>
    public Result SetEnabled(EnvironmentKey environment, bool isEnabled, DateTimeOffset timestamp, Guid causedBy) =>
        StateIn(environment).Match(
            state =>
            {
                if (state.IsEnabled != isEnabled)
                    Raise(new FlagStateChangedEvent(Id, environment, isEnabled, timestamp, causedBy));

                return Result.Success();
            },
            () => Result.Failure(FlagErrors.StateMissing(Key, environment)));

    /// <summary>
    /// Updates the flag's name and/or description. Idempotent — submitting the values it already
    /// has raises no event and leaves <see cref="UpdatedAt"/> untouched, the same rule
    /// <see cref="SetEnabled"/> applies to state. The key is never part of this: there is no
    /// parameter for it, and no other way to change it.
    /// </summary>
    public Result UpdateDetails(string? name, string? description, DateTimeOffset timestamp, Guid causedBy)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(FlagErrors.NameRequired);

        var trimmedName = name.Trim();
        if (trimmedName.Length > MaxNameLength)
            return Result.Failure(FlagErrors.NameTooLong);

        var trimmedDescription = description?.Trim() ?? string.Empty;
        if (trimmedDescription.Length > MaxDescriptionLength)
            return Result.Failure(FlagErrors.DescriptionTooLong);

        if (trimmedName == Name && trimmedDescription == Description)
            return Result.Success();

        Raise(new FlagDetailsChangedEvent(Id, trimmedName, trimmedDescription, timestamp, causedBy));

        return Result.Success();
    }

    /// <summary>Clears events once the repository has durably appended them.</summary>
    internal void ClearUncommittedEvents() => _uncommittedEvents.Clear();

    private void Raise(IFlagEvent @event)
    {
        Apply(@event);
        _uncommittedEvents.Add(@event);
    }

    /// <summary>The one place a fact — fresh or replayed — is folded into this instance's state.</summary>
    private void Apply(IFlagEvent @event)
    {
        switch (@event)
        {
            case FlagCreatedEvent created:
                Id = created.FlagId;
                Key = created.Key;
                Name = created.Name;
                Description = created.Description;
                CreatedAt = created.OccurredAt;
                UpdatedAt = created.OccurredAt;
                break;

            case FlagStateChangedEvent stateChanged:
                var existing = _states.FirstOrDefault(state => state.Environment == stateChanged.Environment);
                if (existing is null)
                    _states.Add(new FlagState(stateChanged.Environment, stateChanged.IsEnabled, [], stateChanged.OccurredAt));
                else
                    existing.Apply(stateChanged.IsEnabled, stateChanged.OccurredAt);
                break;

            case FlagTargetingChangedEvent targetingChanged:
                var targeted = _states.FirstOrDefault(state => state.Environment == targetingChanged.Environment);
                if (targeted is null)
                    _states.Add(new FlagState(targetingChanged.Environment, false, targetingChanged.Segments, targetingChanged.OccurredAt));
                else
                    targeted.ApplyTargeting(targetingChanged.Segments, targetingChanged.OccurredAt);
                break;

            case FlagDetailsChangedEvent detailsChanged:
                Name = detailsChanged.Name;
                Description = detailsChanged.Description;
                UpdatedAt = detailsChanged.OccurredAt;
                break;

            // An event type this build does not know about is a corrupted or forward-incompatible
            // stream, not a fact to skip — folding it silently would advance Version past a dropped
            // change and hand back an aggregate that looks consistent but is not.
            default:
                throw new InvalidOperationException($"Unrecognized flag event type '{@event.GetType()}' on flag {Id}.");
        }

        _version++;
    }
}

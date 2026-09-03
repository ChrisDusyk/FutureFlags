using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FutureFlags.Evaluation;
using OpenFeature;
using OpenFeature.Constant;
using OpenFeature.Model;

namespace FutureFlags.Client.OpenFeature;

/// <summary>
/// A FutureFlags-backed <see cref="FeatureProvider"/>, so an application can read flags through the
/// OpenFeature SDK rather than through this package's own API.
///
/// <para>
/// A thin wrapper over <see cref="IFutureFlagsClient.ResolveAsync"/> rather than a second evaluator.
/// The reasons, variants and error codes it reports come from the shared evaluation source that the
/// server and the Node client also answer from, which is what stops an OpenFeature consumer and a
/// native one being told different things about the same flag.
/// </para>
/// <para>
/// Evaluation stays in this process: the client holds a ruleset and refreshes it in the background,
/// so a resolution is a lookup rather than a request even though OpenFeature's API is asynchronous.
/// </para>
/// </summary>
/// <param name="client">The client to resolve through. Take one from the container after calling
/// <c>AddFutureFlags</c>.</param>
public sealed class FutureFlagsProvider(IFutureFlagsClient client) : FeatureProvider
{
    private static readonly Metadata ProviderMetadata = new("FutureFlags");

    // A field rather than the parameter directly, because the guard is real work: this constructor
    // is public on a public type, so it is reachable without a container in front of it.
    private readonly IFutureFlagsClient _client = client ?? throw new ArgumentNullException(nameof(client));

    /// <inheritdoc />
    public override Metadata GetMetadata() => ProviderMetadata;

    /// <inheritdoc />
    public override async Task<ResolutionDetails<bool>> ResolveBooleanValueAsync(
        string flagKey,
        bool defaultValue,
        EvaluationContext? context = null,
        CancellationToken cancellationToken = default)
    {
        // Never throws, which the specification requires of a flag evaluation (1.4.10) and which
        // this client already guaranteed: an unreachable service resolves as PROVIDER_NOT_READY
        // rather than as an exception.
        var resolution = await _client
            .ResolveAsync(flagKey, ToFlagContext(context), cancellationToken)
            .ConfigureAwait(false);

        if (resolution.ErrorCode is not null)
        {
            return Failed(flagKey, defaultValue, ToErrorType(resolution.ErrorCode), resolution.ErrorMessage);
        }

        // A flag whose value is not boolean is a type mismatch, not a coerced answer. There are
        // none today — every flag this platform can author is boolean — but the ruleset already
        // carries a value type, so this is the honest reading rather than an unreachable branch
        // pretending to be one.
        if (resolution.Value.Kind != FlagValueKind.Boolean)
        {
            return Mismatched(flagKey, defaultValue, resolution.Value.Kind, "boolean");
        }

        return new ResolutionDetails<bool>(
            flagKey,
            resolution.Value.Boolean,
            ErrorType.None,
            resolution.Reason,
            resolution.Variant);
    }

    /// <inheritdoc />
    public override Task<ResolutionDetails<string>> ResolveStringValueAsync(
        string flagKey,
        string defaultValue,
        EvaluationContext? context = null,
        CancellationToken cancellationToken = default) =>
        UnsupportedAsync(flagKey, defaultValue, "string", cancellationToken);

    /// <inheritdoc />
    public override Task<ResolutionDetails<int>> ResolveIntegerValueAsync(
        string flagKey,
        int defaultValue,
        EvaluationContext? context = null,
        CancellationToken cancellationToken = default) =>
        UnsupportedAsync(flagKey, defaultValue, "number", cancellationToken);

    /// <inheritdoc />
    public override Task<ResolutionDetails<double>> ResolveDoubleValueAsync(
        string flagKey,
        double defaultValue,
        EvaluationContext? context = null,
        CancellationToken cancellationToken = default) =>
        UnsupportedAsync(flagKey, defaultValue, "number", cancellationToken);

    /// <inheritdoc />
    public override Task<ResolutionDetails<Value>> ResolveStructureValueAsync(
        string flagKey,
        Value defaultValue,
        EvaluationContext? context = null,
        CancellationToken cancellationToken = default) =>
        UnsupportedAsync(flagKey, defaultValue, "object", cancellationToken);

    /// <summary>
    /// Loads the first ruleset, so a client created after <c>SetProviderAsync</c> returns is
    /// answering from real flags rather than from defaults.
    ///
    /// <para>
    /// A failure here is swallowed rather than thrown. The specification lets an initialize failure
    /// terminate abnormally, but this client's whole posture is that a flag service being
    /// unreachable must not take down the application reading it — so the provider comes up and
    /// every resolution says PROVIDER_NOT_READY until a background refresh succeeds.
    /// </para>
    /// </summary>
    public override async Task InitializeAsync(
        EvaluationContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (FutureFlagsException)
        {
            // Deliberately absorbed — see the note above.
        }
    }

    /// <summary>
    /// OpenFeature's context, as this platform's.
    ///
    /// <para>
    /// <c>TargetingKey</c> becomes the context key. Attribute values that this platform cannot
    /// represent — structures, lists, and datetimes — are dropped rather than rejected, matching
    /// what the server's own OFREP routes do with the same context: absent, and absent never
    /// matches. Failing instead would mean one unrelated field in a context stops every flag
    /// resolving.
    /// </para>
    /// </summary>
    internal static FlagContext ToFlagContext(EvaluationContext? context)
    {
        if (context is null)
        {
            return FlagContext.Empty;
        }

        var attributes = new Dictionary<string, AttributeValue>(StringComparer.Ordinal);

        foreach (var pair in context.AsDictionary())
        {
            // The targeting key is carried separately and is not also an attribute.
            if (string.Equals(pair.Key, EvaluationContext.Empty.TargetingKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(pair.Key, "targetingKey", StringComparison.Ordinal))
            {
                continue;
            }

            if (TryConvert(pair.Value, out var value))
            {
                attributes[pair.Key] = value;
            }
        }

        return new FlagContext(context.TargetingKey, attributes);
    }

    private static bool TryConvert(Value? value, out AttributeValue converted)
    {
        converted = AttributeValue.OfText(string.Empty);

        if (value is null || value.IsNull)
        {
            return false;
        }

        if (value.IsBoolean)
        {
            converted = AttributeValue.OfBoolean(value.AsBoolean!.Value);
            return true;
        }

        if (value.IsNumber)
        {
            converted = AttributeValue.OfNumber(value.AsDouble!.Value);
            return converted.IsRepresentable;
        }

        if (value.IsString)
        {
            converted = AttributeValue.OfText(value.AsString);
            return converted.IsRepresentable;
        }

        // A datetime renders as ISO-8601 text, which is the only form three runtimes compare the
        // same way — nothing downstream parses it back.
        if (value.IsDateTime)
        {
            converted = AttributeValue.OfText(value.AsDateTime!.Value.ToString("O"));
            return converted.IsRepresentable;
        }

        // Structures and lists: nothing here can hold them.
        return false;
    }

    private static ErrorType ToErrorType(string errorCode) => errorCode switch
    {
        EvaluationErrorCode.FlagNotFound => ErrorType.FlagNotFound,
        EvaluationErrorCode.ProviderNotReady => ErrorType.ProviderNotReady,
        EvaluationErrorCode.TypeMismatch => ErrorType.TypeMismatch,
        EvaluationErrorCode.ParseError => ErrorType.ParseError,
        EvaluationErrorCode.InvalidContext => ErrorType.InvalidContext,
        EvaluationErrorCode.TargetingKeyMissing => ErrorType.TargetingKeyMissing,
        EvaluationErrorCode.ProviderFatal => ErrorType.ProviderFatal,
        _ => ErrorType.General,
    };

    private static ResolutionDetails<T> Failed<T>(
        string flagKey,
        T defaultValue,
        ErrorType errorType,
        string? errorMessage) =>
        new(flagKey, defaultValue, errorType, EvaluationReason.Error, variant: null, errorMessage);

    private static ResolutionDetails<T> Mismatched<T>(
        string flagKey,
        T defaultValue,
        FlagValueKind actual,
        string requested) =>
        new(
            flagKey,
            defaultValue,
            ErrorType.TypeMismatch,
            EvaluationReason.Error,
            variant: null,
            $"The flag '{flagKey}' holds a {actual} value and was asked for a {requested} one.");

    /// <summary>
    /// Every non-boolean resolution, answered honestly rather than coerced.
    ///
    /// <para>
    /// Every flag this platform can author is boolean, so a caller asking for a string or an object
    /// is asking for something that does not exist. <c>TYPE_MISMATCH</c> with the caller's own
    /// default is what the specification asks for and what a caller can act on; inventing a value
    /// from a boolean would be worse than useless. The flag is still looked up first, so a
    /// misspelled key is reported as missing rather than as a type problem.
    /// </para>
    /// </summary>
    private async Task<ResolutionDetails<T>> UnsupportedAsync<T>(
        string flagKey,
        T defaultValue,
        string requested,
        CancellationToken cancellationToken)
    {
        var resolution = await _client
            .ResolveAsync(flagKey, FlagContext.Empty, cancellationToken)
            .ConfigureAwait(false);

        return resolution.ErrorCode is not null
            ? Failed(flagKey, defaultValue, ToErrorType(resolution.ErrorCode), resolution.ErrorMessage)
            : Mismatched(flagKey, defaultValue, resolution.Value.Kind, requested);
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using FeatureFlags.Evaluation;

namespace FeatureFlags.Domain.Tests.Evaluation;

/// <summary>
/// Loads <c>shared/evaluation/conformance/*.json</c>.
///
/// <para>
/// The point of these files is that three runtimes answer the same way, so they are read here with
/// <see cref="RulesetJson.Options"/> — the production settings — rather than anything a test
/// invented. A vector the real parser cannot read is a finding, not a fixture problem.
/// </para>
/// </summary>
internal static class ConformanceVectors
{
    private static readonly JsonSerializerOptions Options = BuildOptions();

    public static IReadOnlyList<TCase> Load<TCase>(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "conformance", fileName);
        var file = JsonSerializer.Deserialize<VectorFile<TCase>>(File.ReadAllText(path), Options)
            ?? throw new InvalidOperationException($"'{fileName}' deserialized to nothing.");

        // An empty vector file would make every conformance test vacuously pass, which is the one
        // failure mode these tests cannot afford.
        Assert.NotEmpty(file.Cases);

        return file.Cases;
    }

    private static JsonSerializerOptions BuildOptions()
    {
        // The vectors carry a prose "note" the wire types have no property for.
        return new JsonSerializerOptions(RulesetJson.Options)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        };
    }

    private sealed class VectorFile<TCase>
    {
        public int Version { get; set; }

        public string? Note { get; set; }

        public List<TCase> Cases { get; set; } = [];
    }
}

/// <summary>The shape of a context in a vector file, which is the shape of one on the wire.</summary>
internal sealed class ContextVector
{
    public string? Key { get; set; }

    public Dictionary<string, AttributeValue>? Attributes { get; set; }

    public FlagContext ToContext() => new(Key, Attributes);
}

internal sealed class SegmentCase
{
    public string Name { get; set; } = string.Empty;

    public RulesetSegment? Segment { get; set; }

    public ContextVector? Context { get; set; }

    public bool Matches { get; set; }

    public override string ToString() => Name;
}

internal sealed class FlagCase
{
    public string Name { get; set; } = string.Empty;

    public Ruleset? Ruleset { get; set; }

    public ContextVector? Context { get; set; }

    public Dictionary<string, bool> Expected { get; set; } = [];

    public override string ToString() => Name;
}

using FeatureFlags.Evaluation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace FeatureFlags.Client.Tests;

/// <summary>
/// Reading a flag for a particular person — the reason this package pulls a ruleset rather than a
/// map of answers.
/// </summary>
public class FlagContextTests
{
    private const string Key =
        "ffs_dev_f992c8928754087a_7f097037aa14d671f4317df877989f05f5309c1323ecb24dab4be5597f40db10";

    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    private static Ruleset Targeted() => new(
        "dev",
        [
            new RulesetFlag("new-checkout", true, ["pro-users"]),
            new RulesetFlag("dark-mode", true, []),
        ],
        [
            new RulesetSegment(
                "pro-users",
                ["user-17"],
                ["user-99"],
                [new RulesetCondition("plan", ConditionOperatorNames.EqualTo, [AttributeValue.OfText("pro")])]),
        ]);

    private static (IFeatureFlagClient Client, StubHandler Server) Build(
        Action<FeatureFlagsOptions>? configure = null)
    {
        var server = new StubHandler().AnswersWithRuleset(Targeted(), "\"v1\"");

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        services.AddFeatureFlags(options =>
        {
            options.BaseAddress = new Uri("https://flags.example.com");
            options.SdkKey = Key;
            configure?.Invoke(options);
        });
        // Replaces the handler every typed client in this collection would otherwise build for
        // itself, which is the only one here.
        services.ConfigureHttpClientDefaults(builder => builder.ConfigurePrimaryHttpMessageHandler(() => server));

        var provider = services.BuildServiceProvider();

        return (provider.GetRequiredService<IFeatureFlagClient>(), server);
    }

    [Fact]
    public async Task IsEnabledAsync_ForAMatchingContext_ShouldBeTrue()
    {
        var (client, _) = Build();

        var enabled = await client.IsEnabledAsync(
            "new-checkout",
            FlagContext.For("user-1").With("plan", "pro"),
            TestContext.Current.CancellationToken);

        Assert.True(enabled);
    }

    [Fact]
    public async Task IsEnabledAsync_ForANonMatchingContext_ShouldBeFalse()
    {
        var (client, _) = Build();

        var enabled = await client.IsEnabledAsync(
            "new-checkout",
            FlagContext.For("user-1").With("plan", "free"),
            TestContext.Current.CancellationToken);

        Assert.False(enabled);
    }

    [Fact]
    public async Task IsEnabledAsync_WithNoContext_ShouldReadATargetedFlagAsOff()
    {
        // The compatible reading: a caller who has not said who is asking has not described anybody
        // the segment could contain.
        var (client, _) = Build();

        Assert.False(await client.IsEnabledAsync("new-checkout", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsEnabledAsync_WithNoContext_ShouldStillAnswerAnUntargetedFlagNormally()
    {
        var (client, _) = Build();

        Assert.True(await client.IsEnabledAsync("dark-mode", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsEnabledAsync_ShouldMatchOnAnIncludedKeyWithoutAnyAttributes()
    {
        var (client, _) = Build();

        Assert.True(await client.IsEnabledAsync(
            "new-checkout", FlagContext.For("user-17"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsEnabledAsync_ShouldLetAnExcludedKeyBeatAMatchingAttribute()
    {
        var (client, _) = Build();

        Assert.False(await client.IsEnabledAsync(
            "new-checkout",
            FlagContext.For("user-99").With("plan", "pro"),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsEnabledAsync_ShouldTakeAttributesFromTheDefaultContext()
    {
        var (client, _) = Build(options =>
            options.DefaultContext = FlagContext.For(null).With("plan", "pro"));

        Assert.True(await client.IsEnabledAsync(
            "new-checkout", FlagContext.For("user-1"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsEnabledAsync_ShouldLetAPerCallAttributeBeatTheDefault()
    {
        var (client, _) = Build(options =>
            options.DefaultContext = FlagContext.For(null).With("plan", "pro"));

        Assert.False(await client.IsEnabledAsync(
            "new-checkout",
            FlagContext.For("user-1").With("plan", "free"),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsEnabledAsync_ForAnUnknownKey_ShouldReturnTheCallersDefaultWhateverTheContext()
    {
        var (client, _) = Build();

        Assert.True(await client.IsEnabledAsync(
            "never-heard-of-it",
            FlagContext.For("user-17"),
            defaultValue: true,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsEnabledAsync_ShouldEvaluateWithoutAskingTheServerAgain()
    {
        // The property that makes per-user evaluation affordable: the ruleset is fetched once and
        // every context after that is answered in this process.
        var (client, server) = Build();

        for (var i = 0; i < 10; i++)
        {
            await client.IsEnabledAsync(
                "new-checkout",
                FlagContext.For($"user-{i}").With("plan", "pro"),
                TestContext.Current.CancellationToken);
        }

        Assert.Equal(1, server.CallCount);
    }
}

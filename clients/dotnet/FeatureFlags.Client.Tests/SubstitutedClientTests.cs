using FeatureFlags.Evaluation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FeatureFlags.Client.Tests;

/// <summary>
/// A consumer is allowed to put their own <see cref="IFeatureFlagClient"/> in — a stub in an
/// integration test, a decorator that records what was asked, a wrapper with its own defaults.
/// Registering one must not stop the host from starting, and the refresh loop must drive whatever
/// is registered rather than reaching past it for the implementation this package ships.
/// </summary>
public class SubstitutedClientTests
{
    private const string Key =
        "ffs_dev_f992c8928754087a_7f097037aa14d671f4317df877989f05f5309c1323ecb24dab4be5597f40db10";

    private sealed class StubClient : IFeatureFlagClient
    {
        public int RefreshCount;

        public Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> IsEnabledAsync(string key, bool defaultValue, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> IsEnabledAsync(string key, FlagContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> IsEnabledAsync(
            string key,
            FlagContext context,
            bool defaultValue,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref RefreshCount);

            return Task.CompletedTask;
        }
    }

    /// <summary>A substitute that fails the way an unreachable server would.</summary>
    private sealed class FailingClient : IFeatureFlagClient
    {
        public Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> IsEnabledAsync(string key, bool defaultValue, CancellationToken cancellationToken = default) =>
            Task.FromResult(defaultValue);

        public Task<bool> IsEnabledAsync(string key, FlagContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> IsEnabledAsync(
            string key,
            FlagContext context,
            bool defaultValue,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(defaultValue);

        public Task RefreshAsync(CancellationToken cancellationToken = default) =>
            throw new FeatureFlagsException("Nope.");
    }

    private static IHost BuildHost(Action<IServiceCollection> register, bool throwOnStartupFailure = false)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddFeatureFlags(options =>
        {
            options.BaseAddress = new Uri("https://flags.example.com");
            options.SdkKey = Key;
            options.ThrowOnStartupFailure = throwOnStartupFailure;
        });

        register(builder.Services);

        return builder.Build();
    }

    [Fact]
    public async Task AReplacedClient_ShouldNotStopTheHostFromStarting()
    {
        var stub = new StubClient();

        using var host = BuildHost(services => services.AddSingleton<IFeatureFlagClient>(stub));

        // Previously an InvalidCastException here: the refresh service took the interface and cast
        // it to the concrete client, which a substitute is not.
        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.Same(stub, host.Services.GetRequiredService<IFeatureFlagClient>());
    }

    [Fact]
    public async Task AReplacedClient_ShouldBeTheOneRefreshedAtStartup()
    {
        var stub = new StubClient();

        using var host = BuildHost(services => services.AddSingleton<IFeatureFlagClient>(stub));

        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        // Not the shipped client behind its back: a stub exists precisely so nothing reaches the
        // network, and priming the real one anyway would defeat that.
        Assert.Equal(1, stub.RefreshCount);
    }

    [Fact]
    public async Task AFailingRefresh_ShouldNotStopTheHostByDefault()
    {
        using var host = BuildHost(services => services.AddSingleton<IFeatureFlagClient, FailingClient>());

        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AFailingRefresh_ShouldStopTheHostWhenAskedTo()
    {
        using var host = BuildHost(
            services => services.AddSingleton<IFeatureFlagClient, FailingClient>(),
            throwOnStartupFailure: true);

        await Assert.ThrowsAsync<FeatureFlagsException>(
            () => host.StartAsync(TestContext.Current.CancellationToken));
    }
}

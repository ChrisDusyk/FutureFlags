using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FutureFlags.Client.Tests;

public class RegistrationTests
{
    private const string Key =
        "ffs_dev_f992c8928754087a_7f097037aa14d671f4317df877989f05f5309c1323ecb24dab4be5597f40db10";

    private static ServiceProvider Build(Action<FutureFlagsOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFutureFlags(configure);

        return services.BuildServiceProvider();
    }

    private static FutureFlagsOptions Resolve(Action<FutureFlagsOptions> configure) =>
        Build(configure).GetRequiredService<IOptions<FutureFlagsOptions>>().Value;

    [Fact]
    public void AddFutureFlags_ShouldRegisterTheClientAsASingleton()
    {
        using var provider = Build(options =>
        {
            options.BaseAddress = new Uri("https://flags.example.com");
            options.SdkKey = Key;
        });

        var first = provider.GetRequiredService<IFutureFlagsClient>();
        var second = provider.GetRequiredService<IFutureFlagsClient>();

        // The snapshot lives on the client, so a second instance would mean a second set of polls.
        Assert.Same(first, second);
    }

    [Fact]
    public void AddFutureFlags_ShouldRegisterTheRefreshService()
    {
        using var provider = Build(options =>
        {
            options.BaseAddress = new Uri("https://flags.example.com");
            options.SdkKey = Key;
        });

        Assert.Single(provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>());
    }

    [Fact]
    public void AddFutureFlags_FromConfiguration_ShouldBindTheSettings()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BaseAddress"] = "https://flags.example.com",
                ["SdkKey"] = Key,
                ["PollingInterval"] = "00:01:00"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFutureFlags(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<FutureFlagsOptions>>().Value;

        Assert.Equal(new Uri("https://flags.example.com"), options.BaseAddress);
        Assert.Equal(Key, options.SdkKey);
        Assert.Equal(TimeSpan.FromMinutes(1), options.PollingInterval);
    }

    [Fact]
    public void Options_ShouldDefaultToThirtySecondPollingAndNotThrowAtStartup()
    {
        var options = Resolve(o =>
        {
            o.BaseAddress = new Uri("https://flags.example.com");
            o.SdkKey = Key;
        });

        Assert.Equal(TimeSpan.FromSeconds(30), options.PollingInterval);
        Assert.Equal(TimeSpan.FromSeconds(10), options.Timeout);
        Assert.False(options.ThrowOnStartupFailure);
    }

    [Theory]
    [InlineData(null, Key, "BaseAddress is required")]
    [InlineData("https://flags.example.com", null, "SdkKey is required")]
    [InlineData("https://flags.example.com", "", "SdkKey is required")]
    [InlineData("https://flags.example.com", "not-a-key", "does not look like one")]
    [InlineData("https://flags.example.com", "eyJhbGciOiJFUzI1NiJ9.e.s", "does not look like one")]
    [InlineData("ftp://flags.example.com", Key, "must be http or https")]
    public void Validation_ShouldRejectAMisconfiguration(string? baseAddress, string? sdkKey, string expected)
    {
        var exception = Assert.Throws<OptionsValidationException>(() => Resolve(options =>
        {
            options.BaseAddress = baseAddress is null ? null : new Uri(baseAddress);
            options.SdkKey = sdkKey;
        }));

        Assert.Contains(expected, exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validation_ShouldRejectANonPositivePollingInterval(int seconds)
    {
        Assert.Throws<OptionsValidationException>(() => Resolve(options =>
        {
            options.BaseAddress = new Uri("https://flags.example.com");
            options.SdkKey = Key;
            options.PollingInterval = TimeSpan.FromSeconds(seconds);
        }));
    }

    [Fact]
    public void AddFutureFlags_WithoutAConfigureDelegate_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ServiceCollection().AddFutureFlags((Action<FutureFlagsOptions>)null!));
    }
}

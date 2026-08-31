using Microsoft.Extensions.DependencyInjection;

namespace FutureFlags.Client.Redis.Tests;

/// <summary>
/// The wiring that does not need a live Redis: <c>AddFutureFlagsRedisCache</c>'s own argument
/// checking. <see cref="RedisCachedFutureFlagsClientTests"/> covers the parts that do need one.
/// </summary>
public class RegistrationTests
{
    private const string SdkKey =
        "ffs_dev_f992c8928754087a_7f097037aa14d671f4317df877989f05f5309c1323ecb24dab4be5597f40db10";

    private static ServiceCollection WithBaseClientRegistered()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFutureFlags(options =>
        {
            options.BaseAddress = new Uri("https://flags.example.com");
            options.SdkKey = SdkKey;
        });

        return services;
    }

    [Fact]
    public void WithoutAddFutureFlagsFirst_ShouldThrow()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddFutureFlagsRedisCache());

        Assert.Contains("AddFutureFlags", exception.Message);
    }

    [Fact]
    public void WithoutAConfigureDelegate_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ServiceCollection().AddFutureFlagsRedisCache((Action<FutureFlagsRedisCacheOptions>)null!));
    }

    [Fact]
    public void OnANullServiceCollection_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(
            () => ((IServiceCollection)null!).AddFutureFlagsRedisCache());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ANullOrEmptyKeyPrefix_ShouldThrow(string? keyPrefix)
    {
        var services = WithBaseClientRegistered();

        Assert.Throws<ArgumentException>(
            () => services.AddFutureFlagsRedisCache(o => o.KeyPrefix = keyPrefix!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveFailSafeMaxDuration_ShouldThrow(int seconds)
    {
        var services = WithBaseClientRegistered();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => services.AddFutureFlagsRedisCache(o => o.FailSafeMaxDuration = TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void ANegativeFailSafeThrottleDuration_ShouldThrow()
    {
        var services = WithBaseClientRegistered();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => services.AddFutureFlagsRedisCache(o => o.FailSafeThrottleDuration = TimeSpan.FromSeconds(-1)));
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    public void AnEagerRefreshThresholdOutsideZeroToOne_ShouldThrow(float threshold)
    {
        var services = WithBaseClientRegistered();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => services.AddFutureFlagsRedisCache(o => o.EagerRefreshThreshold = threshold));
    }
}

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenFeature;

namespace FutureFlags.Client.OpenFeature;

/// <summary>
/// Registers <see cref="FutureFlagsProvider"/>, so an application reads flags through the
/// OpenFeature SDK.
///
/// <code>
/// services.AddFutureFlags(options =&gt;
/// {
///     options.BaseAddress = new Uri("https://flags.example.com");
///     options.SdkKey = configuration["FutureFlags:SdkKey"];
/// });
///
/// services.AddFutureFlagsOpenFeatureProvider();
/// </code>
/// </summary>
public static class FutureFlagsOpenFeatureExtensions
{
    /// <summary>
    /// Registers the provider in the container.
    ///
    /// <para>
    /// Separate from <c>AddFutureFlags</c> rather than folded into it: registering a provider is a
    /// process-wide act — the OpenFeature API is a singleton, and setting a default provider on it
    /// affects every part of an application, including code that never asked for FutureFlags. That
    /// should be something a caller writes down, not something that happens because they installed
    /// a package.
    /// </para>
    /// <para>
    /// Call <c>AddFutureFlags</c> first; this resolves the client it registers.
    /// </para>
    /// </summary>
    public static IServiceCollection AddFutureFlagsOpenFeatureProvider(this IServiceCollection services)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.TryAddSingleton<FutureFlagsProvider>();

        return services;
    }

    /// <summary>
    /// Makes FutureFlags the OpenFeature API's default provider, and waits for its first ruleset.
    ///
    /// <para>
    /// Awaited because <c>SetProviderAsync</c> runs the provider's <c>InitializeAsync</c>, and
    /// returning before that finishes would mean the first evaluations an application makes are
    /// answered from defaults. It still cannot throw over an unreachable server — see
    /// <see cref="FutureFlagsProvider.InitializeAsync"/>.
    /// </para>
    /// </summary>
    public static Task UseFutureFlagsAsync(this IServiceProvider provider)
    {
        if (provider is null)
        {
            throw new ArgumentNullException(nameof(provider));
        }

        return Api.Instance.SetProviderAsync(provider.GetRequiredService<FutureFlagsProvider>());
    }
}

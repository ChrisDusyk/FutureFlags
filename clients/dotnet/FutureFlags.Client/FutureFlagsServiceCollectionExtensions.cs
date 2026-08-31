using System;
using System.Net.Http.Headers;
using System.Reflection;
using FutureFlags.Client.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FutureFlags.Client;

/// <summary>
/// Registers <see cref="IFutureFlagsClient"/>.
///
/// <code>
/// services.AddFutureFlags(options =>
/// {
///     options.BaseAddress = new Uri("https://flags.example.com");
///     options.SdkKey = configuration["FutureFlags:SdkKey"];
/// });
/// </code>
/// </summary>
public static class FutureFlagsServiceCollectionExtensions
{
    private static readonly string Version =
        typeof(FutureFlagsServiceCollectionExtensions).GetTypeInfo().Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    /// <summary>Registers the client, configured in code.</summary>
    public static IServiceCollection AddFutureFlags(
        this IServiceCollection services,
        Action<FutureFlagsOptions> configure)
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        services.Configure(configure);

        return AddFutureFlagsCore(services);
    }

    /// <summary>
    /// Binds from configuration — an <c>appsettings.json</c> section, environment variables, or
    /// anything else bound into it.
    ///
    /// <code>
    /// {
    ///   "FutureFlags": {
    ///     "BaseAddress": "https://flags.example.com",
    ///     "SdkKey": "ffs_prod_…"
    ///   }
    /// }
    /// </code>
    /// </summary>
    public static IServiceCollection AddFutureFlags(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        services.Configure<FutureFlagsOptions>(configuration);

        return AddFutureFlagsCore(services);
    }

    private static IServiceCollection AddFutureFlagsCore(IServiceCollection services)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<FutureFlagsOptions>, FutureFlagsOptionsValidator>());

        services.AddHttpClient<EvaluationApiClient>((provider, http) =>
        {
            var options = provider.GetRequiredService<IOptions<FutureFlagsOptions>>().Value;

            // Trailing slash: without one, Uri composition drops the last path segment, so an
            // installation served under a sub-path would silently lose it.
            var baseAddress = options.BaseAddress!.AbsoluteUri;
            http.BaseAddress = new Uri(baseAddress.EndsWith("/", StringComparison.Ordinal)
                ? baseAddress
                : baseAddress + "/");

            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", options.SdkKey);
            http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("FutureFlags.Client", Version));

            // The client's own Timeout bounds a refresh, including the wait for a free slot, so
            // this one is a backstop rather than the mechanism.
            http.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
        });

        services.TryAddSingleton<IFutureFlagsClient, FutureFlagsClient>();

        // Hosted, so a hosted application warms the snapshot at startup and polls. Nothing depends
        // on it: the client refreshes lazily as well, which is what keeps this package usable from
        // a console application or a .NET Framework service with no host at all.
        services.AddHostedService<FutureFlagsRefreshService>();

        return services;
    }
}

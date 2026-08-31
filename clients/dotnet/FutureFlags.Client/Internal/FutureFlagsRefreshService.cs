using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FutureFlags.Client.Internal;

/// <summary>
/// Keeps the snapshot warm for a hosted application: one fetch before the application starts
/// serving, then one every <see cref="FutureFlagsOptions.PollingInterval"/>.
///
/// <para>
/// The client refreshes lazily too, so this is not what makes it correct — it is what stops the
/// first request of the process from paying for the first fetch, and what keeps a flag change
/// arriving in an application that is idle rather than only in one under load.
/// </para>
/// </summary>
internal sealed class FutureFlagsRefreshService(
    // The interface, and only the interface. A consumer is allowed to register their own
    // implementation — a stub in a test, a decorator that records what was asked — and this
    // used to cast to the concrete client, which turned that into an InvalidCastException
    // while the host was starting. Whatever is registered is what gets refreshed.
    IFutureFlagsClient client,
    IOptions<FutureFlagsOptions> options,
    ILogger<FutureFlagsRefreshService> logger) : BackgroundService
{
    private readonly FutureFlagsOptions _options = options.Value;

    /// <summary>
    /// The first fetch is awaited before the loop so that <c>ThrowOnStartupFailure</c> can mean
    /// what it says — a BackgroundService that throws from StartAsync stops the host.
    /// </summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await RefreshAsync(rethrow: _options.ThrowOnStartupFailure, cancellationToken).ConfigureAwait(false);

        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.PollingInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Shutting down. Not a failure, and not something to log about.
                return;
            }

            await RefreshAsync(rethrow: false, stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// <see cref="IFutureFlagsClient.RefreshAsync"/> reports a failure, which is right for somebody
    /// who asked for one explicitly and wrong for a loop: a polling loop that died on one bad
    /// response would leave the snapshot frozen at whatever it last held, silently, for the life of
    /// the process. So the loop catches, and only startup rethrows — and only when asked to.
    /// </summary>
    private async Task RefreshAsync(bool rethrow, CancellationToken cancellationToken)
    {
        try
        {
            await client.RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down mid-fetch. Not a failure, and not something to log about.
        }
        catch (Exception exception) when (!rethrow)
        {
            logger.LogWarning(
                exception,
                "Could not refresh feature flags. Callers continue with the flags last read.");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Stopping feature flag refresh.");

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}

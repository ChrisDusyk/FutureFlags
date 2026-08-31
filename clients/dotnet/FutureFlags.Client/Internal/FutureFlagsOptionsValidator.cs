using System;
using Microsoft.Extensions.Options;

namespace FutureFlags.Client.Internal;

/// <summary>
/// Catches the misconfigurations that would otherwise surface as a 401 or a 404 at the first read,
/// somewhere far from the line that caused them.
/// </summary>
internal sealed class FutureFlagsOptionsValidator : IValidateOptions<FutureFlagsOptions>
{
    /// <summary>
    /// Matched loosely on purpose. This is here to catch a value that is obviously not a key — an
    /// empty string, a JWT pasted by mistake, the name of an environment variable that did not
    /// expand. Whether the key is *valid* is the server's to say, and only it can.
    /// </summary>
    private const string KeyPrefix = "ffs_";

    public ValidateOptionsResult Validate(string? name, FutureFlagsOptions options)
    {
        if (options.BaseAddress is null)
        {
            return ValidateOptionsResult.Fail(
                "FutureFlags: BaseAddress is required. It is the origin the console is on, " +
                "for example https://flags.example.com.");
        }

        if (!options.BaseAddress.IsAbsoluteUri)
        {
            return ValidateOptionsResult.Fail(
                $"FutureFlags: BaseAddress must be absolute, including the scheme. '{options.BaseAddress}' is not.");
        }

        if (options.BaseAddress.Scheme != Uri.UriSchemeHttp && options.BaseAddress.Scheme != Uri.UriSchemeHttps)
        {
            return ValidateOptionsResult.Fail(
                $"FutureFlags: BaseAddress must be http or https. '{options.BaseAddress.Scheme}' is neither.");
        }

        if (string.IsNullOrWhiteSpace(options.SdkKey))
        {
            return ValidateOptionsResult.Fail(
                "FutureFlags: SdkKey is required. Issue one in the console under " +
                "Organization → Environments.");
        }

        if (!options.SdkKey!.StartsWith(KeyPrefix, StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Fail(
                $"FutureFlags: SdkKey does not look like one — it should begin with '{KeyPrefix}'.");
        }

        if (options.PollingInterval <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("FutureFlags: PollingInterval must be greater than zero.");
        }

        if (options.Timeout <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("FutureFlags: Timeout must be greater than zero.");
        }

        return ValidateOptionsResult.Success;
    }
}

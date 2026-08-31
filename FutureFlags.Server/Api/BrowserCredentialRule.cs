using FutureFlags.Domain.SdkKeys;
using FutureFlags.Domain.Shared;
using Microsoft.Net.Http.Headers;

namespace FutureFlags.Server.Api;

/// <summary>
/// Refuses a secret SDK key presented from a browser.
///
/// <para>
/// This is the enforcement, and CORS is not. CORS decides what a browser is willing to hand back to
/// a script — it is the browser's rule, applied in the browser, and it does nothing about a
/// credential that has already been published in a bundle for anyone to copy out and use from
/// wherever they like. Refusing the credential happens on this side, where refusing means something.
/// </para>
///
/// <para>
/// <c>Origin</c> is the signal because it is a forbidden header: a browser sets it and script cannot
/// change it. Its presence therefore means a browser sent the request, which means the key it
/// carries has been shipped to one — which for a secret key is already the mistake, whatever the
/// response to this particular request turns out to be.
/// </para>
/// </summary>
public static class BrowserCredentialRule
{
    /// <summary>
    /// Whether the credential on this request may be used from where it was used.
    ///
    /// <para>
    /// A server-side caller sends no <c>Origin</c> and is never affected. One that sets the header
    /// by hand is refused, which is strange to do and explained clearly when it happens — the
    /// alternative is a rule that anything can opt out of by omitting a header.
    /// </para>
    /// </summary>
    public static Result Check(HttpContext context)
    {
        var fromABrowser = context.Request.Headers.ContainsKey(HeaderNames.Origin);

        return fromABrowser && !context.User.HasPublishableSdkKey()
            ? Result.Failure(SdkKeyErrors.SecretKeyFromBrowser)
            : Result.Success();
    }
}

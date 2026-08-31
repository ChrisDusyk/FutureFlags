using System.Text.Json;
using FutureFlags.Server.Features.SdkKeys.IssueSdkKey;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace FutureFlags.Server.Tests.Features.SdkKeys;

/// <summary>
/// Pins the shape of the issue endpoint's answer: 201, a body carrying the token, and deliberately
/// no <c>Location</c>.
///
/// <para>
/// There is no route that answers with an SDK key — the token in that body is the only time it
/// exists — so a <c>Location</c> pointing at one would be an invitation to go and fetch it again.
/// Passing a null URI to <see cref="Results.Created(string?, object?)"/> is how that is expressed,
/// and this is here because "does that throw?" is a reasonable thing to wonder about a null being
/// handed to something named after the header it sets. It does not, and the assertion below is
/// what keeps that from being a matter of opinion.
/// </para>
/// </summary>
public class IssueSdkKeyResultTests
{
    private static readonly IssueSdkKeyResponse Response = new(
        Guid.CreateVersion7(),
        "CI",
        "secret",
        "dev",
        "ffs_dev_f992c8928754087a_7f097037aa14d671f4317df877989f05f5309c1323ecb24dab4be5597f40db10",
        new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero));

    private static async Task<HttpContext> ExecuteAsync(IResult result)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider()
        };

        context.Response.Body = new MemoryStream();

        await result.ExecuteAsync(context);

        context.Response.Body.Position = 0;

        return context;
    }

    [Fact]
    public async Task TheIssueResult_ShouldBe201WithNoLocation()
    {
        var context = await ExecuteAsync(Results.Created((string?)null, Response));

        Assert.Equal(StatusCodes.Status201Created, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey(HeaderNames.Location));
    }

    [Fact]
    public async Task TheIssueResult_ShouldCarryTheTokenInItsBody()
    {
        var context = await ExecuteAsync(Results.Created((string?)null, Response));

        var body = await JsonSerializer.DeserializeAsync<IssueSdkKeyResponse>(
            context.Response.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            TestContext.Current.CancellationToken);

        // The one and only time the token is readable. If this ever came back empty, an admin
        // would have issued a credential nobody can use and cannot recover.
        Assert.Equal(Response.Token, body?.Token);
        Assert.Equal(Response.Id, body?.Id);
        Assert.Equal("dev", body?.Environment);
    }
}

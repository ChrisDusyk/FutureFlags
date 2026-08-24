using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FeatureFlags.Evaluation;

namespace FeatureFlags.Client.Redis.Tests;

/// <summary>
/// Stands in for the FeatureFlags server, the same way <c>FeatureFlags.Client.Tests</c>'s own
/// <c>StubHandler</c> does — not shared across the two test assemblies, since neither project
/// references the other's internals and this one is small enough not to be worth exporting.
/// </summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _answers = new();
    private Func<HttpRequestMessage, HttpResponseMessage>? _last;

    public List<HttpRequestMessage> Requests { get; } = [];

    public int CallCount => Requests.Count;

    public StubHandler Answers(Func<HttpRequestMessage, HttpResponseMessage> answer)
    {
        _answers.Enqueue(answer);

        return this;
    }

    /// <summary>
    /// Answers with a ruleset in which every named flag is on or off and reaches everyone. Takes an
    /// anonymous object because these tests are about the cache tier rather than about targeting,
    /// and spelling out a whole ruleset for each would bury what they actually check.
    /// </summary>
    public StubHandler AnswersWithFlags(string environment, object flags, string etag) =>
        Answers(_ =>
        {
            var ruleset = new Ruleset(
                environment,
                [.. flags.GetType().GetProperties()
                    .Select(property => new RulesetFlag(property.Name, (bool)property.GetValue(flags)!, []))],
                []);

            var json = System.Text.Json.JsonSerializer.Serialize(ruleset, RulesetJson.Options);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            response.Headers.ETag = new EntityTagHeaderValue(etag);

            return response;
        });

    public StubHandler AnswersNotModified(string etag) => Answers(_ =>
    {
        var response = new HttpResponseMessage(HttpStatusCode.NotModified);
        response.Headers.ETag = new EntityTagHeaderValue(etag);

        return response;
    });

    public StubHandler Throws() => Answers(_ => throw new HttpRequestException("The stub refused."));

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);

        if (_answers.Count > 0)
        {
            _last = _answers.Dequeue();
        }

        var answer = _last ?? throw new InvalidOperationException("The stub was asked before it was told what to say.");

        return Task.FromResult(answer(request));
    }
}

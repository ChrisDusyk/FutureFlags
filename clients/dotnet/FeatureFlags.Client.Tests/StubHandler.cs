using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FeatureFlags.Evaluation;

namespace FeatureFlags.Client.Tests;

/// <summary>
/// Stands in for the FeatureFlags server. Records what it was asked so a test can assert on the
/// request as well as the answer — the <c>Authorization</c> and <c>If-None-Match</c> headers are
/// half of what this package does.
/// </summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _answers = new();

    public List<HttpRequestMessage> Requests { get; } = [];

    public int CallCount => Requests.Count;

    /// <summary>How long the stub takes to answer. Stands in for a server that accepts the
    /// connection and then does not respond.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    /// <summary>The last answer queued repeats once the queue runs dry, so a polling test does not
    /// have to enumerate every poll it might make.</summary>
    private Func<HttpRequestMessage, HttpResponseMessage>? _last;

    public StubHandler Answers(Func<HttpRequestMessage, HttpResponseMessage> answer)
    {
        _answers.Enqueue(answer);

        return this;
    }

    /// <summary>
    /// Answers with a ruleset in which every named flag is on or off and reaches everyone. Takes an
    /// anonymous object — <c>new { checkout = true }</c> — because most tests here are about the
    /// request and the snapshot rather than about targeting, and spelling out a whole ruleset for
    /// them would bury what they are actually checking.
    /// </summary>
    public StubHandler AnswersWithFlags(string environment, object flags, string etag)
    {
        var ruleset = new Ruleset(
            environment,
            [.. flags.GetType().GetProperties()
                .Select(property => new RulesetFlag(property.Name, (bool)property.GetValue(flags)!, []))],
            []);

        return AnswersWithRuleset(ruleset, etag);
    }

    /// <summary>Answers with exactly this ruleset, for the tests that are about targeting.</summary>
    public StubHandler AnswersWithRuleset(Ruleset ruleset, string etag)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(ruleset, RulesetJson.Options);

        return Answers(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            response.Headers.ETag = new EntityTagHeaderValue(etag);

            return response;
        });
    }

    public StubHandler AnswersNotModified(string etag) => Answers(_ =>
    {
        var response = new HttpResponseMessage(HttpStatusCode.NotModified);
        response.Headers.ETag = new EntityTagHeaderValue(etag);

        return response;
    });

    public StubHandler AnswersWithStatus(HttpStatusCode status) =>
        Answers(_ => new HttpResponseMessage(status));

    public StubHandler Throws() => Answers(_ => throw new HttpRequestException("The stub refused."));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);

        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        if (_answers.Count > 0)
        {
            _last = _answers.Dequeue();
        }

        var answer = _last ?? throw new InvalidOperationException("The stub was asked before it was told what to say.");

        return answer(request);
    }
}

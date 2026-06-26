namespace Api.Tests;

using System.Diagnostics.Metrics;
using System.Net;
using Microsoft.AspNetCore.Http;
using Trellis.Testing.AspNetCore;

// Pins the SLI middleware ordering: UseServiceLevelIndicator() must run BEFORE UseTrellisIdempotency so
// a request the idempotency middleware short-circuits (a same-key replay, which never reaches the
// handler) is still measured. Ordered after idempotency, the replay would short-circuit first and emit
// no metric — silently undercounting the request surface. A source-only parity check cannot see the
// ordering, so this asserts the runtime behaviour. (A validation 422 would NOT distinguish the order:
// MVC produces it during model binding, downstream of the middleware pipeline either way.)
[Collection(TestWebApplicationFactoryCollectionFixture.Id)]
public class ServiceLevelIndicatorOrderingTests
{
    private const string CreateUrl = "api/Todos?api-version=2026-03-26";
    private readonly TestWebApplicationFactoryFixture _factory;

    public ServiceLevelIndicatorOrderingTests(TestWebApplicationFactoryFixture factory, ITestOutputHelper output)
    {
        _factory = factory;
        _factory.OutputHelper = output;
    }

    [Fact]
    public async Task Idempotency_replay_short_circuit_still_emits_an_sli_measurement()
    {
        var client = _factory.CreateClientWithActor("sli-replay-user", "todos:create", "todos:read");
        var body = new { title = "Measure my replay", dueDate = DateTime.UtcNow.AddDays(7), tag = "sli" };
        var idempotencyKey = Guid.NewGuid().ToString();

        // The first call is a fresh invocation that runs the whole pipeline (measured either way).
        var first = await client.PostJsonWithKeyAsync(CreateUrl, body, idempotencyKey, TestContext.Current.CancellationToken);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        // Start capturing only the replay's SLI emissions.
        var replayStatusCodes = new List<int>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == "Trellis.SLI" && instrument.Name == "operation.duration")
                    l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "http.response.status.code" && tag.Value is int status)
                    replayStatusCodes.Add(status);
            }
        });
        listener.Start();

        // The same-key retry is short-circuited by UseTrellisIdempotency — it never reaches the handler,
        // proven by the replay marker header.
        var replay = await client.PostJsonWithKeyAsync(CreateUrl, body, idempotencyKey, TestContext.Current.CancellationToken);
        replay.StatusCode.Should().Be(HttpStatusCode.Created);
        replay.Headers.Contains("Idempotent-Replayed").Should().BeTrue();

        replayStatusCodes.Should().Contain(
            StatusCodes.Status201Created,
            "the SLI middleware must run before idempotency so replays are still measured");
    }
}

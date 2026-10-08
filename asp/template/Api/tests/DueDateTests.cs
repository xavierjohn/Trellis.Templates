namespace Api.Tests;

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TodoSample.AntiCorruptionLayer;
using TodoSample.Domain;
using Trellis.Testing.AspNetCore;

[Collection(TestWebApplicationFactoryCollectionFixture.Id)]
public class DueDateTests(TestWebApplicationFactoryFixture factory, ITestOutputHelper output)
{
    public static TheoryData<string, bool, string?, HttpStatusCode, string?> DateCases()
    {
        string[] versions =
        [
#if (UseApiVersioning)
            "2026-03-26",
#endif
            "2026-12-01",
        ];
        var cases = new TheoryData<string, bool, string?, HttpStatusCode, string?>();
        foreach (var version in versions)
            foreach (var update in new[] { false, true })
            {
                var success = update ? HttpStatusCode.OK : HttpStatusCode.Created;
                cases.Add(version, update, "2099-01-01T12:00:00Z", success, "2099-01-01T12:00:00Z");
                cases.Add(version, update, "2099-01-01T12:00:00+05:30", success, "2099-01-01T06:30:00Z");
                cases.Add(version, update, "2099-01-01T12:00:00-07:00", success, "2099-01-01T19:00:00Z");
                cases.Add(version, update, "2099-01-01T12:00:00+00:00", success, "2099-01-01T12:00:00Z");
                cases.Add(version, update, "2099-01-01T12:00:00", HttpStatusCode.UnprocessableEntity, null);
                cases.Add(version, update, "2099-01-01", HttpStatusCode.UnprocessableEntity, null);
                cases.Add(version, update, "not-a-date", HttpStatusCode.UnprocessableEntity, null);
                cases.Add(version, update, "0001-01-01T00:00:00Z", HttpStatusCode.UnprocessableEntity, null);
                cases.Add(version, update, null, HttpStatusCode.UnprocessableEntity, null);
            }
        return cases;
    }

    [Theory]
    [MemberData(nameof(DateCases))]
    public async Task Create_and_update_require_timezone_and_preserve_the_utc_instant(
        string version, bool update, string? dueDate, HttpStatusCode expectedStatus, string? expectedUtc)
    {
        factory.OutputHelper = output;
        using var client = factory.CreateClientWithActor("date-owner", "todos:create", "todos:read", "todos:update");
        var cancellationToken = TestContext.Current.CancellationToken;
        var url = Url("api/Todos", version);
        const string originalDate = "2099-02-01T00:00:00Z";
        using var request = new HttpRequestMessage(update ? HttpMethod.Put : HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new { title = "Timezone contract", dueDate }),
        };
        if (update)
        {
            using var created = await client.PostJsonIdempotentAsync(
                url, new { title = "Original", dueDate = originalDate }, cancellationToken);
            created.StatusCode.Should().Be(HttpStatusCode.Created);
            request.RequestUri = created.Headers.Location;
            request.Headers.IfMatch.Add(created.Headers.ETag!);
        }
        else
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        using var response = await client.SendAsync(request, cancellationToken);

        response.StatusCode.Should().Be(expectedStatus);
        if (expectedUtc is null)
        {
            var problem = await response.Content.ReadAsAsyncWithAssertion<ValidationProblemDetails>();
            problem.Errors.Should().ContainKey("dueDate");
            if (update)
                await AssertPersistedDate(client, request.RequestUri!, originalDate);
            return;
        }

        await AssertDate(response, expectedUtc);
        await AssertPersistedDate(client, update ? request.RequestUri! : response.Headers.Location!, expectedUtc);
    }

    [Theory]
#if (UseApiVersioning)
    [InlineData("2026-03-26", false)]
    [InlineData("2026-03-26", true)]
#endif
    [InlineData("2026-12-01", false)]
    [InlineData("2026-12-01", true)]
    public async Task Update_future_validation_uses_the_utc_instant_not_the_offset_clock(
        string version, bool future)
    {
        factory.OutputHelper = output;
        using var client = factory.CreateClientWithActor("date-owner", "todos:create", "todos:read", "todos:update");
        var cancellationToken = TestContext.Current.CancellationToken;
        using var created = await client.PostJsonIdempotentAsync(
            Url("api/Todos", version),
            new { title = "Original", dueDate = "2099-02-01T00:00:00Z" },
            cancellationToken);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var now = DateTimeOffset.FromUnixTimeSeconds(TimeProvider.System.GetUtcNow().ToUnixTimeSeconds());
        var date = now.AddMinutes(future ? 30 : -30).ToOffset(TimeSpan.FromHours(future ? -1 : 1));
        using var request = new HttpRequestMessage(HttpMethod.Put, created.Headers.Location)
        {
            Content = JsonContent.Create(new { title = "Offset deadline", dueDate = date.ToString("O", CultureInfo.InvariantCulture) }),
        };
        request.Headers.IfMatch.Add(created.Headers.ETag!);

        using var response = await client.SendAsync(request, cancellationToken);

        response.StatusCode.Should().Be(future ? HttpStatusCode.OK : HttpStatusCode.UnprocessableEntity);
        var expected = future ? date.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) : "2099-02-01T00:00:00Z";
        await AssertPersistedDate(client, created.Headers.Location!, expected);
    }

    [Fact]
    public async Task Due_date_filters_use_normalized_utc_instants_with_trellis_interceptors()
    {
        factory.OutputHelper = output;
        var actorId = "date-filter-" + Guid.NewGuid().ToString("N");
        using var client = factory.CreateClientWithActor(actorId, "todos:create");
        var cancellationToken = TestContext.Current.CancellationToken;
        var cutoff = new DateTime(2099, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var dueDate = DueDate.Create(cutoff);
        var dates = new[]
        {
            new { title = "Before", dueDate = "2099-01-01T17:29:59.999+05:30" },
            new { title = "At", dueDate = "2099-01-01T05:00:00-07:00" },
            new { title = "After", dueDate = "2099-01-01T12:00:00.001Z" },
        };
        foreach (var date in dates)
        {
            using var response = await client.PostJsonIdempotentAsync(Url("api/Todos", "2026-12-01"), date, cancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var todos = context.TodoItems.AsNoTracking().Where(item => item.CreatedByActorId == actorId);
        var before = await todos.Where(item => item.DueDate.Value < cutoff).ToListAsync(cancellationToken);
        var at = await todos.Where(item => item.DueDate == dueDate).ToListAsync(cancellationToken);
        var after = await todos.Where(item => item.DueDate.Value > cutoff).ToListAsync(cancellationToken);
        before.Select(item => item.Title.Value).Should().Equal("Before");
        at.Select(item => item.Title.Value).Should().Equal("At");
        after.Select(item => item.Title.Value).Should().Equal("After");
        at.Single().DueDate.Value.Should().Be(cutoff);
        before.Concat(at).Concat(after).Should().OnlyContain(item => item.DueDate.Value.Kind == DateTimeKind.Utc);
    }

    private static async Task AssertPersistedDate(HttpClient client, Uri uri, string expectedUtc)
    {
        using var response = await client.GetAsync(uri, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertDate(response, expectedUtc);
    }

    private static async Task AssertDate(HttpResponseMessage response, string expectedUtc)
    {
        using var body = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);
        var actual = body.RootElement.GetProperty("dueDate").GetDateTime();
        actual.Kind.Should().Be(DateTimeKind.Utc);
        actual.Should().Be(DateTime.Parse(expectedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    private static string Url(string path, string version)
    {
#if (UseApiVersioning)
        return $"{path}?api-version={version}";
#else
        return path;
#endif
    }
}

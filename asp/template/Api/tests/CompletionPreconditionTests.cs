namespace Api.Tests;

using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
#if (UseApiVersioning)
using TodoSample.Api.v2026_03_26.Models;
#else
using TodoSample.Api.v2026_12_01.Models;
#endif
using Trellis.Asp;
using Trellis.Testing.AspNetCore;

[Collection(TestWebApplicationFactoryCollectionFixture.Id)]
public class CompletionPreconditionTests(TestWebApplicationFactoryFixture factory, ITestOutputHelper output)
{
    [Theory]
#if (UseApiVersioning)
    [InlineData("2026-03-26", null, HttpStatusCode.OK)]
    [InlineData("2026-03-26", "current", HttpStatusCode.OK)]
    [InlineData("2026-03-26", "wildcard", HttpStatusCode.OK)]
    [InlineData("2026-03-26", "stale", HttpStatusCode.PreconditionFailed)]
    [InlineData("2026-03-26", "weak", HttpStatusCode.PreconditionFailed)]
    [InlineData("2026-03-26", "empty", HttpStatusCode.PreconditionFailed)]
    [InlineData("2026-03-26", "malformed", HttpStatusCode.PreconditionFailed)]
#endif
    [InlineData("2026-12-01", null, HttpStatusCode.OK)]
    [InlineData("2026-12-01", "current", HttpStatusCode.OK)]
    [InlineData("2026-12-01", "wildcard", HttpStatusCode.OK)]
    [InlineData("2026-12-01", "stale", HttpStatusCode.PreconditionFailed)]
    [InlineData("2026-12-01", "weak", HttpStatusCode.PreconditionFailed)]
    [InlineData("2026-12-01", "empty", HttpStatusCode.PreconditionFailed)]
    [InlineData("2026-12-01", "malformed", HttpStatusCode.PreconditionFailed)]
    public async Task Complete_honors_optional_If_Match_before_mutating(
        string version, string? headerKind, HttpStatusCode expectedStatus)
    {
        factory.OutputHelper = output;
        using var client = factory.CreateClientWithActor("owner", "todos:create", "todos:read", "todos:complete");
        var cancellationToken = TestContext.Current.CancellationToken;
        using var createdResponse = await client.PostJsonIdempotentAsync(
            Url("api/Todos", version),
            new { title = "Conditional completion", dueDate = DateTime.UtcNow.AddDays(7) },
            cancellationToken);
        createdResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createdResponse.Content.ReadAsAsyncWithAssertion<TodoResponse>();
        var etag = createdResponse.Headers.ETag
            ?? throw new InvalidOperationException("Create response must include an ETag.");
        var resourceUrl = Url($"api/Todos/{created.Id}", version);

        using var request = new HttpRequestMessage(
            HttpMethod.Post, Url($"api/Todos/{created.Id}/complete", version));
        if (headerKind is not null)
        {
            var header = headerKind switch
            {
                "current" => etag.ToString(),
                "wildcard" => "*",
                "stale" => "\"stale-etag\"",
                "weak" => $"W/{etag.Tag}",
                "empty" => " ", // HttpClient drops zero-length values; whitespace preserves the empty field.
                "malformed" => "not-an-etag",
                _ => throw new ArgumentOutOfRangeException(nameof(headerKind)),
            };
            request.Headers.TryAddWithoutValidation("If-Match", header).Should().BeTrue();
        }

        using var response = await client.SendAsync(request, cancellationToken);
        response.StatusCode.Should().Be(expectedStatus);
        using var readResponse = await client.GetAsync(resourceUrl, cancellationToken);
        readResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var persisted = await readResponse.Content.ReadAsAsyncWithAssertion<TodoResponse>();

        if (expectedStatus == HttpStatusCode.OK)
        {
            persisted.Status.Should().Be("Completed");
            persisted.CompletedAt.Should().NotBeNull();
            return;
        }

        var problem = await response.Content.ReadAsAsyncWithAssertion<ProblemDetails>();
        problem.Status.Should().Be((int)HttpStatusCode.PreconditionFailed);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        persisted.Status.Should().Be(created.Status);
        persisted.CompletedAt.Should().BeNull();
        persisted.LastModified.Should().Be(created.LastModified);
        readResponse.Headers.ETag.Should().Be(etag);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Present_empty_If_Match_is_not_an_unconditional_request(string header)
    {
        var request = new DefaultHttpContext().Request;
        request.Headers.IfMatch = header;

        ETagHelper.ParseIfMatch(request).Should().BeEmpty();
    }

    [Theory]
#if (UseApiVersioning)
    [InlineData("2026-03-26")]
#endif
    [InlineData("2026-12-01")]
    public async Task Complete_with_stale_If_Match_preserves_not_found(string version)
    {
        factory.OutputHelper = output;
        using var client = factory.CreateClientWithActor("owner", "todos:complete");
        using var request = new HttpRequestMessage(
            HttpMethod.Post, Url($"api/Todos/{Guid.NewGuid()}/complete", version));
        request.Headers.TryAddWithoutValidation("If-Match", "\"stale-etag\"").Should().BeTrue();

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
#if (UseApiVersioning)
    [InlineData("2026-03-26")]
#endif
    [InlineData("2026-12-01")]
    public async Task Complete_with_stale_If_Match_preserves_resource_authorization(string version)
    {
        factory.OutputHelper = output;
        using var owner = factory.CreateClientWithActor("owner", "todos:create", "todos:read");
        var cancellationToken = TestContext.Current.CancellationToken;
        using var createdResponse = await owner.PostJsonIdempotentAsync(
            Url("api/Todos", version),
            new { title = "Owner-only completion", dueDate = DateTime.UtcNow.AddDays(7) },
            cancellationToken);
        createdResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createdResponse.Content.ReadAsAsyncWithAssertion<TodoResponse>();
        using var other = factory.CreateClientWithActor("other", "todos:complete");
        using var request = new HttpRequestMessage(
            HttpMethod.Post, Url($"api/Todos/{created.Id}/complete", version));
        request.Headers.TryAddWithoutValidation("If-Match", "\"stale-etag\"").Should().BeTrue();

        using var response = await other.SendAsync(request, cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var readResponse = await owner.GetAsync(
            Url($"api/Todos/{created.Id}", version), cancellationToken);
        var persisted = await readResponse.Content.ReadAsAsyncWithAssertion<TodoResponse>();
        persisted.Status.Should().Be("Active");
        readResponse.Headers.ETag.Should().Be(createdResponse.Headers.ETag);
    }

#if (UseApiVersioning)
    private static string Url(string path, string version) => $"{path}?api-version={version}";
#else
    private static string Url(string path, string version) => path;
#endif
}

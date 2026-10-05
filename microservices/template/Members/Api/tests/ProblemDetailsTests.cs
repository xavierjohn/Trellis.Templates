using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Members.Api.Tests;

public class ProblemDetailsTests(MembersApiFactory factory) : IClassFixture<MembersApiFactory>
{
    [Fact]
    public async Task Route_misses_have_the_standard_Trellis_error_envelope()
    {
        var response = await factory.CreateClient().GetAsync("/no-such-route", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("code").GetString().Should().Be("error.unspecified");
        problem.GetProperty("kind").GetString().Should().Be("not-found");
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }
}
using System.Net;
using System.Net.Http.Json;
using ProjectTrackerTemplate.Members.Domain;
using Trellis.Authorization;
using Trellis.Testing.AspNetCore;

namespace Members.Api.Tests;

// HTTP-level integration tests for the Members host: the JWT trust boundary, the resource-authorization
// pipeline (HideExistence -> 404 cross-tenant), and the invite command, all driven through the real
// pipeline against in-memory SQLite.
public class MembersApiTests(MembersApiFactory factory) : IClassFixture<MembersApiFactory>
{
    private const string Version = "2026-03-26";

    [Fact]
    public async Task Get_member_without_the_required_permission_is_403()
    {
        var client = factory.CreateClientWithActor(Actor("alice", "acme"));

        var response = await client.GetAsync($"/api/members/acme-alice?api-version={Version}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_own_tenant_member_is_200_with_the_member_body()
    {
        var client = factory.CreateClientWithActor(Actor("alice", "acme", Permissions.MembersRead));

        var response = await client.GetAsync($"/api/members/acme-alice?api-version={Version}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MemberBody>(TestContext.Current.CancellationToken);
        body!.Id.Should().Be("acme-alice");
        body.TenantId.Should().Be("acme");
    }

    [Fact]
    public async Task Get_cross_tenant_member_is_404_to_hide_existence()
    {
        var client = factory.CreateClientWithActor(Actor("alice", "acme", Permissions.MembersRead));

        var response = await client.GetAsync($"/api/members/globex-carol?api-version={Version}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Invite_member_is_200_and_returns_a_tenant_scoped_id()
    {
        var client = factory.CreateClientWithActor(Actor("alice", "acme", Permissions.MembersInvite));
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/members?api-version={Version}")
        {
            Content = JsonContent.Create(new { email = "newhire@acme.example", role = "contributor" }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<InviteResponse>(TestContext.Current.CancellationToken);
        body!.Id.Should().Be("acme-newhire");
    }

    private static Actor Actor(string id, string tenant, params string[] permissions) =>
        new(id, new HashSet<string>(permissions), new HashSet<string>(), new Dictionary<string, string> { ["tenant_id"] = tenant });

    private sealed record InviteResponse(string Id);

    private sealed record MemberBody(string Id, string TenantId, string Email, string Role);
}

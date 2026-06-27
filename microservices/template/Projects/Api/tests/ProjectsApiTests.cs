using System.Net;
using System.Net.Http.Json;
using ProjectTrackerTemplate.Projects.Domain;
using Trellis.Authorization;
using Trellis.Testing.AspNetCore;

namespace Projects.Api.Tests;

// HTTP-level integration tests for the Projects host: the JWT trust boundary, the resource-authorization
// pipeline (cross-tenant -> 403, unlike Members' HideExistence 404), and the team read model — all driven
// through the real pipeline against in-memory SQLite.
public class ProjectsApiTests(ProjectsApiFactory factory) : IClassFixture<ProjectsApiFactory>
{
    private const string Version = "2026-03-26";

    [Fact]
    public async Task Get_project_without_the_required_permission_is_403()
    {
        var client = factory.CreateClientWithActor(Actor("alice", "acme"));

        var response = await client.GetAsync($"/api/projects/acme-p1?api-version={Version}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_own_tenant_project_is_200_with_the_project_body()
    {
        var client = factory.CreateClientWithActor(Actor("alice", "acme", Permissions.ProjectsRead));

        var response = await client.GetAsync($"/api/projects/acme-p1?api-version={Version}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ProjectBody>(TestContext.Current.CancellationToken);
        body!.Id.Should().Be("acme-p1");
        body.TenantId.Should().Be("acme");
    }

    [Fact]
    public async Task Get_cross_tenant_project_is_403()
    {
        var client = factory.CreateClientWithActor(Actor("alice", "acme", Permissions.ProjectsRead));

        var response = await client.GetAsync($"/api/projects/globex-p1?api-version={Version}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_team_is_200_and_empty_before_any_member_is_invited()
    {
        var client = factory.CreateClientWithActor(Actor("alice", "acme", Permissions.ProjectsRead));

        var response = await client.GetAsync($"/api/team?api-version={Version}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var team = await response.Content.ReadFromJsonAsync<TeamMemberBody[]>(TestContext.Current.CancellationToken);
        team.Should().BeEmpty();
    }

    private static Actor Actor(string id, string tenant, params string[] permissions) =>
        new(id, new HashSet<string>(permissions), new HashSet<string>(), new Dictionary<string, string> { ["tenant_id"] = tenant });

    private sealed record ProjectBody(string Id, string TenantId, string Title);

    private sealed record TeamMemberBody(string MemberId, string TenantId, string Role);
}

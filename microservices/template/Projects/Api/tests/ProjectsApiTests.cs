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

    [Fact]
    public async Task Update_project_without_if_match_is_428()
    {
        var client = factory.CreateClientWithActor(Actor("alice", "acme", Permissions.ProjectsWrite));
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/projects/acme-p1?api-version={Version}")
        {
            Content = JsonContent.Create(new { title = "Q2 launch", description = "Coordinate Q2 launch." }),
        };

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);
    }

    [Fact]
    public async Task Update_project_with_a_stale_if_match_is_412()
    {
        var client = factory.CreateClientWithActor(Actor("alice", "acme", Permissions.ProjectsWrite));
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/projects/acme-p1?api-version={Version}")
        {
            Content = JsonContent.Create(new { title = "Q2 launch", description = "Coordinate Q2 launch." }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"stale-etag\"");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
    }

    [Fact]
    public async Task Update_project_with_a_matching_if_match_is_200_and_restamps_the_etag()
    {
        var client = factory.CreateClientWithActor(Actor("alice", "acme", Permissions.ProjectsRead, Permissions.ProjectsWrite));

        var read = await client.GetAsync($"/api/projects/acme-p1?api-version={Version}", TestContext.Current.CancellationToken);
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        var currentETag = read.Headers.ETag;
        currentETag.Should().NotBeNull();

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/projects/acme-p1?api-version={Version}")
        {
            Content = JsonContent.Create(new { title = "Q2 launch", description = "Coordinate Q2 launch." }),
        };
        request.Headers.IfMatch.Add(currentETag!);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag.Should().NotBeNull();
        response.Headers.ETag!.Tag.Should().NotBe(currentETag!.Tag);
    }

    [Fact]
    public async Task Update_project_with_an_empty_title_is_422()
    {
        var client = factory.CreateClientWithActor(Actor("alice", "acme", Permissions.ProjectsWrite));
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/projects/acme-p1?api-version={Version}")
        {
            Content = JsonContent.Create(new { title = "", description = "Coordinate Q2 launch." }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"any\"");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task List_projects_is_keyset_paginated_with_a_next_link()
    {
        var client = factory.CreateClientWithActor(Actor("alice", "acme", Permissions.ProjectsRead));

        // The acme tenant is seeded with two projects, so limit=1 yields one item plus a next cursor.
        var firstPage = await client.GetFromJsonAsync<PagedProjects>(
            $"/api/projects?limit=1&api-version={Version}", TestContext.Current.CancellationToken);
        firstPage.Should().NotBeNull();
        firstPage!.Items.Should().HaveCount(1);
        firstPage.Items[0].TenantId.Should().Be("acme");
        firstPage.Next.Should().NotBeNull();
        firstPage.Next!.Cursor.Should().NotBeNullOrEmpty();
        firstPage.Next.Href.Should().Contain("cursor=");
        firstPage.Next.Href.Should().Contain($"api-version={Version}", "PageUrl must inject the api-version so the next-page link resolves");

        var secondPage = await client.GetFromJsonAsync<PagedProjects>(
            $"/api/projects?cursor={firstPage.Next.Cursor}&limit=1&api-version={Version}",
            TestContext.Current.CancellationToken);
        secondPage.Should().NotBeNull();
        secondPage!.Items.Should().HaveCount(1);
        secondPage.Items[0].Id.Should().NotBe(firstPage.Items[0].Id);
    }

    [Fact]
    public async Task List_projects_with_a_malformed_cursor_is_422()
    {
        var client = factory.CreateClientWithActor(Actor("alice", "acme", Permissions.ProjectsRead));

        var response = await client.GetAsync(
            $"/api/projects?cursor=not-a-valid-cursor&api-version={Version}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Get_project_with_a_matching_if_none_match_is_304()
    {
        var client = factory.CreateClientWithActor(Actor("alice", "acme", Permissions.ProjectsRead));

        var read = await client.GetAsync($"/api/projects/acme-p1?api-version={Version}", TestContext.Current.CancellationToken);
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        read.Headers.ETag.Should().NotBeNull();

        var conditional = new HttpRequestMessage(HttpMethod.Get, $"/api/projects/acme-p1?api-version={Version}");
        conditional.Headers.IfNoneMatch.Add(read.Headers.ETag!);

        var response = await client.SendAsync(conditional, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task Update_project_with_prefer_return_minimal_is_204()
    {
        var client = factory.CreateClientWithActor(Actor("alice", "acme", Permissions.ProjectsRead, Permissions.ProjectsWrite));

        var read = await client.GetAsync($"/api/projects/acme-p1?api-version={Version}", TestContext.Current.CancellationToken);
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        var currentETag = read.Headers.ETag;
        currentETag.Should().NotBeNull();

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/projects/acme-p1?api-version={Version}")
        {
            Content = JsonContent.Create(new { title = "Prefer minimal", description = "Edited with Prefer: return=minimal." }),
        };
        request.Headers.IfMatch.Add(currentETag!);
        request.Headers.TryAddWithoutValidation("Prefer", "return=minimal");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private static Actor Actor(string id, string tenant, params string[] permissions) =>
        new(id, new HashSet<string>(permissions), new HashSet<string>(), new Dictionary<string, string> { ["tenant_id"] = tenant });

    private sealed record ProjectBody(string Id, string TenantId, string Title);

    private sealed record PagedProjects(IReadOnlyList<ProjectBody> Items, PageLink? Next);

    private sealed record PageLink(string Cursor, string Href);

    private sealed record TeamMemberBody(string MemberId, string TenantId, string Role);
}

using System.Net;
using System.Net.Http.Json;
using Trellis.Authorization;
using Trellis.Testing.AspNetCore;

namespace Eventing.Tests;

// Hermetic end-to-end test of the cross-service eventing flow. Both hosts run in one process over in-memory
// SQLite, joined by an in-memory broker in place of Azure Service Bus: inviting a member in the Members
// service must, with no synchronous call between services, surface that member in the Projects service's
// team directory — proving outbox -> broker -> inbox -> read-model projection -> read port end to end.
public sealed class MemberInvitedEventingTests : IDisposable
{
    private readonly InMemoryBroker _broker = new();
    private readonly MembersEventingFactory _members;
    private readonly ProjectsEventingFactory _projects;

    public MemberInvitedEventingTests()
    {
        _members = new MembersEventingFactory(_broker);
        _projects = new ProjectsEventingFactory(_broker);
    }

    [Fact]
    public async Task Inviting_a_member_surfaces_them_in_the_projects_team_directory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var membersClient = _members.CreateClientWithActor(
            Actor("alice", "acme", ProjectTrackerTemplate.Members.Domain.Permissions.MembersInvite));
        var projectsClient = _projects.CreateClientWithActor(
            Actor("alice", "acme", ProjectTrackerTemplate.Projects.Domain.Permissions.ProjectsRead));

        var invite = new HttpRequestMessage(HttpMethod.Post, "/api/members?api-version=2026-03-26")
        {
            Content = JsonContent.Create(new { email = "dana@acme.example", role = "contributor" }),
        };
        invite.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var inviteResponse = await membersClient.SendAsync(invite, cancellationToken);
        inviteResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // The outbox relay publishes to the broker and the consumer projects it asynchronously, so poll the
        // team directory until the new member appears (or give up after a generous window).
        TeamMember[] team = [];
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var teamResponse = await projectsClient.GetAsync("/api/team?api-version=2026-03-26", cancellationToken);
            teamResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            team = await teamResponse.Content.ReadFromJsonAsync<TeamMember[]>(cancellationToken) ?? [];
            if (team.Length > 0)
                break;
            await Task.Delay(200, cancellationToken);
        }

        team.Should().ContainSingle().Which.MemberId.Should().Be("acme-dana");
    }

    private static Actor Actor(string id, string tenant, params string[] permissions) =>
        new(id, new HashSet<string>(permissions), new HashSet<string>(), new Dictionary<string, string> { ["tenant_id"] = tenant });

    public void Dispose()
    {
        _members.Dispose();
        _projects.Dispose();
    }

    private sealed record TeamMember(string MemberId, string TenantId, string Role);
}

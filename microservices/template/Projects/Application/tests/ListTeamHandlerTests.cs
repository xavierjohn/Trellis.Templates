using ProjectTrackerTemplate.Projects.Application;
using ProjectTrackerTemplate.Projects.Domain;
using ProjectTrackerTemplate.Projects.ReadModel;
using ProjectTrackerTemplate.SharedKernel;
using Trellis.Authorization;

namespace Projects.Application.Tests;

// The team-directory query resolves the actor's tenant and returns that tenant's slice from the read
// port. Tenant isolation is enforced by the port (ListByTenantAsync); this verifies the handler passes
// the actor's tenant through and returns exactly what the directory yields.
public class ListTeamHandlerTests
{
    [Fact]
    public async Task Returns_the_directory_entries_for_the_actors_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = TenantId.TryCreate("acme").GetValueOrThrow("valid tenant");
        var directory = new StubDirectory(
            [new KnownMember(tenant, "acme-alice", "owner", DateTimeOffset.UnixEpoch)]);
        var actor = new Actor(
            "alice",
            new HashSet<string> { Permissions.ProjectsRead },
            new HashSet<string>(),
            new Dictionary<string, string> { ["tenant_id"] = "acme" });
        var handler = new ListTeamHandler(directory, new StubActorProvider(actor));

        var result = await handler.Handle(new ListTeamQuery(), ct);

        result.Should().BeSuccess()
            .Which.Should().ContainSingle().Which.MemberId.Should().Be("acme-alice");
        directory.RequestedTenant.Should().Be(tenant);
    }

    private sealed class StubDirectory(IReadOnlyList<KnownMember> members) : IKnownMemberDirectory
    {
        public TenantId? RequestedTenant { get; private set; }

        public Task<IReadOnlyList<KnownMember>> ListByTenantAsync(TenantId tenantId, CancellationToken cancellationToken)
        {
            RequestedTenant = tenantId;
            return Task.FromResult(members);
        }
    }

    private sealed class StubActorProvider(Actor actor) : IActorProvider
    {
        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Maybe.From(actor));
    }
}

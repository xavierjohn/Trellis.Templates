using Mediator;
using ProjectTrackerTemplate.Projects.Domain;
using ProjectTrackerTemplate.Projects.ReadModel;
using Trellis;
using Trellis.Authorization;

namespace ProjectTrackerTemplate.Projects.Application;

// List the team (known members) in the actor's tenant. The data is the read model Projects builds from
// Members' MemberInvited events, so this endpoint answers entirely from Projects' OWN store with NO
// synchronous call to the Members service. Tenant-scoped exactly like ListProjectsQuery.
public sealed record ListTeamQuery
    : IQuery<Result<IReadOnlyList<KnownMember>>>, IAuthorize
{
    public IReadOnlyList<string> RequiredPermissions => [Permissions.ProjectsRead];
}

public sealed class ListTeamHandler : IQueryHandler<ListTeamQuery, Result<IReadOnlyList<KnownMember>>>
{
    private readonly IKnownMemberDirectory _directory;
    private readonly IActorProvider _actorProvider;

    public ListTeamHandler(IKnownMemberDirectory directory, IActorProvider actorProvider)
    {
        _directory = directory;
        _actorProvider = actorProvider;
    }

    public async ValueTask<Result<IReadOnlyList<KnownMember>>> Handle(ListTeamQuery query, CancellationToken cancellationToken)
    {
        // The actor + tenant_id are guaranteed by the time the handler runs (IAuthorize + the actor
        // provider's RequiredAttributes), so extract them directly rather than re-checking for absence.
        var actor = (await _actorProvider.GetCurrentActorAsync(cancellationToken))
            .GetValueOrThrow("Actor must be present; the IAuthorize pipeline guarantees it.");
        var tenantId = actor.GetRequiredAttribute<TenantId>("tenant_id")
            .GetValueOrThrow("tenant_id is a required actor attribute; the actor provider guarantees it.");

        var members = await _directory.ListByTenantAsync(tenantId, cancellationToken);

        return Result.Ok(members);
    }
}

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
        var actorMaybe = await _actorProvider.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (!actorMaybe.HasValue)
            return Result.Fail<IReadOnlyList<KnownMember>>(new Error.AuthenticationRequired());

        if (!actorMaybe.Value.GetRequiredAttribute<TenantId>("tenant_id").TryGetValue(out var tenantId))
            return Result.Fail<IReadOnlyList<KnownMember>>(new Error.AuthenticationRequired());

        var members = await _directory.ListByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);

        return Result.Ok(members);
    }
}

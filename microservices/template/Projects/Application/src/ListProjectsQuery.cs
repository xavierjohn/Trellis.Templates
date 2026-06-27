using Mediator;
using ProjectTrackerTemplate.Projects.Domain;
using Trellis;
using Trellis.Authorization;

namespace ProjectTrackerTemplate.Projects.Application;

// List projects in the actor's tenant. Static permission check + ABAC scoping:
// the tenant_id is read from Actor.Attributes and used as a filter, so there is
// no per-row resource authorization — the query itself is tenant-scoped.
//
// Compare with GetProjectQuery, which checks tenant_id at the resource boundary
// (because a single Id could belong to any tenant).
public sealed record ListProjectsQuery
    : IQuery<Result<IReadOnlyList<Project>>>, IAuthorize
{
    public IReadOnlyList<string> RequiredPermissions => [Permissions.ProjectsRead];
}

// Lists projects scoped to the actor's tenant_id. The IActorProvider injection
// gives us the hydrated Actor (including ABAC attributes) without re-parsing
// the JWT. If tenant_id is missing the actor provider already rejected the
// request at the JWT-validation boundary — we never reach here.
//
// Does NOT trigger the per-id projects.resource_loads counter because it
// doesn't call FindByIdAsync — the load-once counter is intentionally per-id only.
public sealed class ListProjectsHandler : IQueryHandler<ListProjectsQuery, Result<IReadOnlyList<Project>>>
{
    private readonly IProjectRepository _repository;
    private readonly IActorProvider _actorProvider;

    public ListProjectsHandler(IProjectRepository repository, IActorProvider actorProvider)
    {
        _repository = repository;
        _actorProvider = actorProvider;
    }

    public async ValueTask<Result<IReadOnlyList<Project>>> Handle(ListProjectsQuery query, CancellationToken cancellationToken)
    {
        // The actor + tenant_id are guaranteed by the time the handler runs (IAuthorize + the actor
        // provider's RequiredAttributes), so extract them directly rather than re-checking for absence.
        var actor = (await _actorProvider.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false))
            .GetValueOrThrow("Actor must be present; the IAuthorize pipeline guarantees it.");
        var tenantId = actor.GetRequiredAttribute<TenantId>("tenant_id")
            .GetValueOrThrow("tenant_id is a required actor attribute; the actor provider guarantees it.");

        var projects = await _repository.ListByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return Result.Ok(projects);
    }
}

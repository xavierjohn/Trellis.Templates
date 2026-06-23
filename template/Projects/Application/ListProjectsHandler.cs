using Mediator;
using ProjectTrackerTemplate.Projects.Domain;
using ProjectTrackerTemplate.Projects.Infrastructure;
using Trellis;
using Trellis.Authorization;

namespace ProjectTrackerTemplate.Projects.Application;

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
        var actorMaybe = await _actorProvider.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (!actorMaybe.HasValue)
            return Result.Fail<IReadOnlyList<Project>>(new Error.AuthenticationRequired());

        if (!actorMaybe.Value.GetRequiredAttribute<TenantId>("tenant_id").TryGetValue(out var tenantId))
            return Result.Fail<IReadOnlyList<Project>>(new Error.AuthenticationRequired());

        var projects = await _repository.ListByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return Result.Ok(projects);
    }
}

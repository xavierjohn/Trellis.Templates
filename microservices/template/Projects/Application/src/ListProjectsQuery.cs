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
// Cursor is an opaque continuation token echoed verbatim from the previous page's `next` link (the
// framework encodes it via CursorCodec); Limit is the client-requested page size, clamped server-side.
public sealed record ListProjectsQuery(string? Cursor, int Limit)
    : IQuery<Result<Page<Project>>>, IAuthorize
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
public sealed class ListProjectsHandler : IQueryHandler<ListProjectsQuery, Result<Page<Project>>>
{
    private const int MaxLimit = 100;
    private const int DefaultLimit = 20;

    private readonly IProjectRepository _repository;
    private readonly IActorProvider _actorProvider;

    public ListProjectsHandler(IProjectRepository repository, IActorProvider actorProvider)
    {
        _repository = repository;
        _actorProvider = actorProvider;
    }

    public async ValueTask<Result<Page<Project>>> Handle(ListProjectsQuery query, CancellationToken cancellationToken)
    {
        var tenantId = await _actorProvider.GetCurrentTenantIdAsync(cancellationToken);

        var pageSize = PageSize.FromRequested(query.Limit <= 0 ? DefaultLimit : query.Limit, MaxLimit);
        var cursor = string.IsNullOrEmpty(query.Cursor) ? (Cursor?)null : new Cursor(query.Cursor);

        // The repository delegates to EF Core's ToPageAsync, which decodes the opaque cursor, applies the
        // keyset seek, over-fetches, and slices the page — a malformed cursor becomes 422, never a throw.
        return await _repository.ListByTenantAsync(tenantId, pageSize, cursor, cancellationToken);
    }
}

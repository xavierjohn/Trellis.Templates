using Mediator;
using ProjectTrackerTemplate.Projects.Domain;
using Trellis;
using Trellis.Authorization;

namespace ProjectTrackerTemplate.Projects.Application;

// Read one project.
//
// IAuthorizeResource<Project>: Authorize is invoked AFTER the pipeline loads the
// project (via ProjectResourceLoader). The check enforces cross-tenant isolation:
// the caller's actor.Attributes["tenant_id"] must match project.TenantId.
// Cross-tenant access returns 403 — Projects is the "operational" cluster that
// tells the caller they're forbidden (the Members cluster, by contrast, uses
// HideExistence to return 404 for cross-tenant requests).
//
// IIdentifyResource<Project, ProjectId>: routes ResourceAuthorizationBehavior
// to ProjectResourceLoader automatically.
//
// IAuthorize: static-permission gate that runs BEFORE resource loading. If the
// actor lacks projects:read the pipeline short-circuits with 403 — the load
// never happens.
public sealed record GetProjectQuery(ProjectId Id)
    : IQuery<Result<Project>>, IAuthorize, IAuthorizeResource<Project>, IIdentifyResource<Project, ProjectId>
{
    public IReadOnlyList<string> RequiredPermissions => [Permissions.ProjectsRead];

    public ProjectId GetResourceId() => Id;

    public Trellis.IResult Authorize(Actor actor, Project resource) =>
        Result.Ensure(
            actor.TryGetAttribute<TenantId>("tenant_id", out var tenantId) && tenantId == resource.TenantId,
            Error.Forbidden.For<Project>("projects.cross_tenant", resource.Id, "Cross-tenant project access is not permitted."));
}

// Reads the SAME Project instance that ResourceAuthorizationBehavior loaded for
// Authorize. Does NOT inject IProjectRepository. The accessor GetRequiredResource()
// returns the in-memory instance — zero second roundtrip, zero second metric tick.
//
// If this handler were rewritten to inject IProjectRepository and call FindByIdAsync
// directly, the projects.resource_loads counter would tick TWICE per request: once
// from the pipeline load (in ResourceAuthorizationBehavior) and once from the
// handler load. That counter is the falsifiable proof of the load-once invariant.
public sealed class GetProjectHandler : IQueryHandler<GetProjectQuery, Result<Project>>
{
    private readonly IAuthorizedResource<GetProjectQuery, Project> _authorized;

    public GetProjectHandler(IAuthorizedResource<GetProjectQuery, Project> authorized) => _authorized = authorized;

    public ValueTask<Result<Project>> Handle(GetProjectQuery query, CancellationToken cancellationToken) =>
        Result.Ok(_authorized.GetRequiredResource()).AsValueTask();
}

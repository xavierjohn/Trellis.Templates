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
    public IReadOnlyList<string> RequiredPermissions => ["projects:read"];

    public ProjectId GetResourceId() => Id;

    public Trellis.IResult Authorize(Actor actor, Project resource) =>
        actor.Attributes.TryGetValue("tenant_id", out var tenantId)
        && string.Equals(tenantId, resource.TenantId, StringComparison.Ordinal)
            ? Result.Ok()
            : Result.Fail(new Error.Forbidden("projects.cross_tenant")
            {
                Detail = "Cross-tenant project access is not permitted.",
            });
}

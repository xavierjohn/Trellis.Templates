using Mediator;
using ProjectTrackerTemplate.Projects.Domain;
using Trellis;
using Trellis.Authorization;

namespace ProjectTrackerTemplate.Projects.Application;

// Edit one project. ResourceAuthorizationBehavior loads the resource then calls
// Authorize(actor, project). Two checks happen here:
//
//   1. Cross-tenant: the actor's tenant_id MUST match project.TenantId
//      (defense in depth — the gateway and actor provider both project
//      tenant_id, but the per-handler check is the last line of defense).
//   2. Ownership: only the project's OwnerId may mutate it.
//
// Anyone failing either check gets 403. The handler then reads the SAME instance
// via IAuthorizedResource<TCommand, Project> — no second repository roundtrip.
public sealed record UpdateProjectCommand(ProjectId Id, string Title, string Description)
    : ICommand<Result<Trellis.Unit>>, IAuthorize, IAuthorizeResource<Project>, IIdentifyResource<Project, ProjectId>
{
    public IReadOnlyList<string> RequiredPermissions => ["projects:write"];

    public ProjectId GetResourceId() => Id;

    public Trellis.IResult Authorize(Actor actor, Project resource)
    {
        if (!actor.TryGetAttribute<TenantId>("tenant_id", out var tenantId)
            || tenantId != resource.TenantId)
        {
            return Result.Fail(new Error.Forbidden("projects.cross_tenant")
            {
                Detail = "Cross-tenant project access is not permitted.",
            });
        }

        if (!string.Equals(resource.OwnerId, actor.Id.Value, StringComparison.Ordinal))
        {
            return Result.Fail(new Error.Forbidden("projects.not_owner")
            {
                Detail = "Only the project's owner can edit it.",
            });
        }

        return Result.Ok();
    }
}

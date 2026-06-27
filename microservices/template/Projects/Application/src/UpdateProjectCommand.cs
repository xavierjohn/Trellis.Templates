using Mediator;
using Microsoft.Extensions.Logging;
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
    public IReadOnlyList<string> RequiredPermissions => [Permissions.ProjectsWrite];

    public ProjectId GetResourceId() => Id;

    public Trellis.IResult Authorize(Actor actor, Project resource) =>
        Result.Ensure(
            actor.TryGetAttribute<TenantId>("tenant_id", out var tenantId) && tenantId == resource.TenantId,
            new Error.Forbidden("projects.cross_tenant") { Detail = "Cross-tenant project access is not permitted." })
        .Ensure(
            _ => string.Equals(resource.OwnerId, actor.Id.Value, StringComparison.Ordinal),
            new Error.Forbidden("projects.not_owner") { Detail = "Only the project's owner can edit it." });
}

// The mutation path. Reads the SAME Project instance ResourceAuthorizationBehavior
// loaded for Authorize, mutates it in place, returns Result.Ok(Unit.Value).
//
// In-memory store quirk: the dictionary holds the SAME reference, so mutating
// the instance from the accessor automatically updates the "stored" project
// without a save call. A real EF service would call SaveChangesAsync (or rely
// on TransactionalCommandBehavior from Trellis.EntityFrameworkCore).
public sealed partial class UpdateProjectHandler : ICommandHandler<UpdateProjectCommand, Result<Trellis.Unit>>
{
    private readonly IAuthorizedResource<UpdateProjectCommand, Project> _authorized;
    private readonly ILogger<UpdateProjectHandler> _logger;

    public UpdateProjectHandler(IAuthorizedResource<UpdateProjectCommand, Project> authorized, ILogger<UpdateProjectHandler> logger)
    {
        _authorized = authorized;
        _logger = logger;
    }

    public ValueTask<Result<Trellis.Unit>> Handle(UpdateProjectCommand command, CancellationToken cancellationToken)
    {
        var project = _authorized.GetRequiredResource();
        project.Update(command.Title, command.Description);

        // Business event for live support, auto-correlated to the request trace.
        LogProjectUpdated(_logger, project.Id.Value);

        return Result.Ok().AsValueTask();
    }

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Project updated: {ProjectId}")]
    private static partial void LogProjectUpdated(ILogger logger, string projectId);
}

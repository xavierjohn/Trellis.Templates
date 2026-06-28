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
public sealed record UpdateProjectCommand(ProjectId Id, ProjectTitle Title, ProjectDescription Description, EntityTagValue[]? IfMatchETags)
    : ICommand<Result<Project>>, IAuthorize, IAuthorizeResource<Project>, IIdentifyResource<Project, ProjectId>
{
    public IReadOnlyList<string> RequiredPermissions => [Permissions.ProjectsWrite];

    public ProjectId GetResourceId() => Id;

    public Trellis.IResult Authorize(Actor actor, Project resource) =>
        Result.Ensure(
            actor.TryGetAttribute<TenantId>("tenant_id", out var tenantId) && tenantId == resource.TenantId,
            Error.Forbidden.For<Project>("projects.cross_tenant", resource.Id, "Cross-tenant project access is not permitted."))
        .Ensure(
            _ => string.Equals(resource.OwnerId, actor.Id.Value, StringComparison.Ordinal),
            Error.Forbidden.For<Project>("projects.not_owner", resource.Id, "Only the project's owner can edit it."));
}

// The mutation path. Reads the SAME Project instance ResourceAuthorizationBehavior loaded for Authorize
// (so there is no second repository round-trip), checks the If-Match precondition, mutates it in place,
// and returns the updated aggregate. The instance is EF-tracked, so TransactionalCommandBehavior (from
// AddTrellisUnitOfWork) commits the change — and re-stamps the ETag — on handler success.
public sealed partial class UpdateProjectHandler : ICommandHandler<UpdateProjectCommand, Result<Project>>
{
    private readonly IAuthorizedResource<UpdateProjectCommand, Project> _authorized;
    private readonly ILogger<UpdateProjectHandler> _logger;

    public UpdateProjectHandler(IAuthorizedResource<UpdateProjectCommand, Project> authorized, ILogger<UpdateProjectHandler> logger)
    {
        _authorized = authorized;
        _logger = logger;
    }

    public ValueTask<Result<Project>> Handle(UpdateProjectCommand command, CancellationToken cancellationToken) =>
        // RequireETag enforces the If-Match precondition against the loaded aggregate's current ETag: a
        // missing header is 428, a stale one is 412 (RFC 9110). Only then is the mutation applied; the unit
        // of work commits it and re-stamps a fresh ETag, which the response returns.
        Result.Ok(_authorized.GetRequiredResource())
            .RequireETag(command.IfMatchETags)
            .Tap(project =>
            {
                project.Update(command.Title, command.Description);

                // Business event for live support, auto-correlated to the request trace.
                LogProjectUpdated(_logger, project.Id);
            })
            .AsValueTask();

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Project updated: {ProjectId}")]
    private static partial void LogProjectUpdated(ILogger logger, string projectId);
}

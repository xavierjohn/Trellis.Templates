using Mediator;
using ProjectTrackerTemplate.Projects.Domain;
using Trellis;
using Trellis.Authorization;

namespace ProjectTrackerTemplate.Projects.Application;

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
        new(Result.Ok(_authorized.GetRequiredResource()));
}

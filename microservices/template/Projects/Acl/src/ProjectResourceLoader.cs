using ProjectTrackerTemplate.Projects.Application;
using ProjectTrackerTemplate.Projects.Domain;
using Trellis;
using Trellis.Authorization;

namespace ProjectTrackerTemplate.Projects.Acl;

// Shared loader used by every command/query that implements
// IIdentifyResource<Project, ProjectId>. Bridges from Maybe<Project> (what the
// repository returns) to Result<Project> (what the authorization pipeline
// expects), translating "not found" into Error.NotFound on the way through.
public sealed class ProjectResourceLoader : SharedResourceLoaderById<Project, ProjectId>
{
    private readonly IProjectRepository _repository;

    public ProjectResourceLoader(IProjectRepository repository) => _repository = repository;

    public override async Task<Result<Project>> GetByIdAsync(ProjectId id, CancellationToken cancellationToken)
    {
        var maybe = await _repository.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return maybe.HasValue
            ? Result.Ok(maybe.Value)
            : Result.Fail<Project>(new Error.NotFound(ResourceRef.For<Project>(id.Value)));
    }
}

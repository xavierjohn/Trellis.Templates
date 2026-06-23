using ProjectTrackerTemplate.Projects.Domain;
using Trellis;

namespace ProjectTrackerTemplate.Projects.Infrastructure;

// Repository contract for Project. Find* returns Maybe<T> per the Trellis
// repo convention; Get* would return Result<T> with Error.NotFound when missing.
public interface IProjectRepository
{
    ValueTask<Maybe<Project>> FindByIdAsync(ProjectId id, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<Project>> ListByTenantAsync(TenantId tenantId, CancellationToken cancellationToken);
}

using ProjectTrackerTemplate.Projects.Domain;
using Trellis;

namespace ProjectTrackerTemplate.Projects.Application;

// Repository contract for Project. Find* returns Maybe<T> per the Trellis
// repo convention; Get* would return Result<T> with Error.NotFound when missing.
public interface IProjectRepository
{
    Task<Maybe<Project>> FindByIdAsync(ProjectId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Project>> ListByTenantAsync(TenantId tenantId, CancellationToken cancellationToken);
}

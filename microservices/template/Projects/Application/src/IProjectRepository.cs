using ProjectTrackerTemplate.Projects.Domain;
using Trellis;

namespace ProjectTrackerTemplate.Projects.Application;

// Repository contract for Project. Find* returns Maybe<T> per the Trellis
// repo convention; Get* would return Result<T> with Error.NotFound when missing.
public interface IProjectRepository
{
    Task<Maybe<Project>> FindByIdAsync(ProjectId id, CancellationToken cancellationToken);

    // Keyset-paginated list scoped to a tenant. Backed by the framework's ToPageAsync seek helper,
    // which owns the cursor decode, the keyset WHERE, the over-fetch, and the Page slice — a malformed
    // cursor surfaces as Error.InvalidInput ("cursor.malformed") => 422, never a throw.
    Task<Result<Page<Project>>> ListByTenantAsync(
        TenantId tenantId,
        PageSize pageSize,
        Cursor? cursor,
        CancellationToken cancellationToken);
}

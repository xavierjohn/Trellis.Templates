using Microsoft.EntityFrameworkCore;
using ProjectTrackerTemplate.Projects.Application;
using ProjectTrackerTemplate.Projects.ReadModel;

namespace ProjectTrackerTemplate.Projects.Acl;

// EF Core implementation of the team-directory read port over the read-model store. Tenant-scoped so
// cross-tenant enumeration is impossible by construction at this layer.
internal sealed class KnownMemberDirectory(ProjectsDbContext db) : IKnownMemberDirectory
{
    public async Task<IReadOnlyList<KnownMember>> ListByTenantAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        await db.KnownMembers
            .Where(km => km.TenantId == tenantId)
            .OrderBy(km => km.MemberId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}

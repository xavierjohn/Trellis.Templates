using Microsoft.EntityFrameworkCore;
using ProjectTrackerTemplate.Members.Application;
using ProjectTrackerTemplate.Members.Domain;
using Trellis.EntityFrameworkCore;

namespace ProjectTrackerTemplate.Members.Acl;

// EF Core implementation. FindByIdAsync, Add, Remove, RemoveByIdAsync, Exists/Count are inherited
// from RepositoryBase<Member, MemberId> (Add only STAGES — the transactional behavior commits);
// only the tenant-scoped list query lives here.
internal sealed class EfMemberRepository : RepositoryBase<Member, MemberId>, IMemberRepository
{
    public EfMemberRepository(MembersDbContext context)
        : base(context)
    {
    }

    public async Task<IReadOnlyList<Member>> ListByTenantAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        await DbSet
            .Where(m => m.TenantId == tenantId)
            .OrderBy(m => m.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}

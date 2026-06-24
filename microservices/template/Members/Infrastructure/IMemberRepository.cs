using ProjectTrackerTemplate.Members.Domain;
using Trellis;

namespace ProjectTrackerTemplate.Members.Infrastructure;

// Repository contract for Member. Find* returns Maybe<T>; ListByTenant returns
// a tenant-scoped list (so cross-tenant enumeration via ListAll is impossible
// by construction at the repository layer).
public interface IMemberRepository
{
    ValueTask<Maybe<Member>> FindByIdAsync(MemberId id, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<Member>> ListByTenantAsync(TenantId tenantId, CancellationToken cancellationToken);

    ValueTask AddAsync(Member member, CancellationToken cancellationToken);
}

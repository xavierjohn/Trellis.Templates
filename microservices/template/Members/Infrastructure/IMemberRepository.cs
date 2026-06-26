using ProjectTrackerTemplate.Members.Domain;
using Trellis;

namespace ProjectTrackerTemplate.Members.Infrastructure;

// Repository contract for Member. FindByIdAsync returns Maybe<T>; ListByTenantAsync returns a
// tenant-scoped list (so cross-tenant enumeration is impossible by construction at this layer).
public interface IMemberRepository
{
    Task<Maybe<Member>> FindByIdAsync(MemberId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Member>> ListByTenantAsync(TenantId tenantId, CancellationToken cancellationToken);

    // Stages the new aggregate; TransactionalCommandBehavior (from AddTrellisUnitOfWork) commits on
    // command-handler success, so handlers never call SaveChanges directly.
    void Add(Member member);
}

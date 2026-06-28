using ProjectTrackerTemplate.Members.Domain;
using Trellis;

namespace ProjectTrackerTemplate.Members.Application;

// Repository contract for Member. FindByIdAsync returns Maybe<T>; ListByTenantAsync returns a
// tenant-scoped list (so cross-tenant enumeration is impossible by construction at this layer).
public interface IMemberRepository
{
    Task<Maybe<Member>> FindByIdAsync(MemberId id, CancellationToken cancellationToken);

    // Lightweight existence check (no-tracking, no materialization) — for the invite duplicate guard, which
    // only needs to know whether the id is taken, not to load the row. Satisfied by RepositoryBase.
    Task<bool> ExistsAsync(MemberId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Member>> ListByTenantAsync(TenantId tenantId, CancellationToken cancellationToken);

    // Stages the new aggregate; TransactionalCommandBehavior (from AddTrellisUnitOfWork) commits on
    // command-handler success, so handlers never call SaveChanges directly.
    void Add(Member member);
}

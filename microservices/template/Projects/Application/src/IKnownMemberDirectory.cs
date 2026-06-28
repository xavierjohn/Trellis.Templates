using ProjectTrackerTemplate.Projects.ReadModel;

namespace ProjectTrackerTemplate.Projects.Application;

// Read port for the team-directory read model. Mirrors IProjectRepository — the interface lives in the
// Application layer and the EF Core implementation in the Acl layer — so the query handler stays free of
// EF Core. The inbound projection (MemberInvitedHandler, in Acl) writes the same store directly, inside
// the inbox's unit of work.
public interface IKnownMemberDirectory
{
    Task<IReadOnlyList<KnownMember>> ListByTenantAsync(TenantId tenantId, CancellationToken cancellationToken);
}

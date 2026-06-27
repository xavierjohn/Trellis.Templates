using Microsoft.EntityFrameworkCore;
using ProjectTrackerTemplate.Projects.ReadModel;
using Trellis.Mediator;

namespace ProjectTrackerTemplate.Projects.Acl;

// Consumer of the MemberInvited published-language contract. Upserts the tenant's team-directory read
// model so Projects knows the member locally. The inbox dispatcher runs this handler INSIDE its unit of
// work and commits the read-model write together with the dedup row in one SaveChanges — so the effect
// is effectively-once even though Service Bus delivery is at-least-once.
//
// Two rules follow from that single-save contract: the handler must NOT call SaveChanges itself (it
// would break atomicity), and it must be safe to re-run — a redelivery whose dedup row lost a concurrency
// race re-runs the handler, so the existence check below keeps the read-model insert idempotent.
internal sealed class MemberInvitedHandler(ProjectsDbContext db) : IIntegrationEventHandler<MemberInvitedIntegrationEvent>
{
    public async ValueTask HandleAsync(MemberInvitedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        // Re-create Projects' own TenantId value object from the wire string. Our producer only ever
        // emits a valid tenant, so a parse failure is unexpected — skip rather than poison the consumer.
        if (!TenantId.TryCreate(integrationEvent.TenantId).TryGetValue(out var tenantId))
            return;

        var alreadyKnown = await db.KnownMembers
            .AnyAsync(km => km.TenantId == tenantId && km.MemberId == integrationEvent.MemberId, cancellationToken)
            ;
        if (alreadyKnown)
            return;

        // Stage only — the inbox dispatcher's single SaveChanges commits this with the dedup row.
        db.KnownMembers.Add(new KnownMember(tenantId, integrationEvent.MemberId, integrationEvent.Role, integrationEvent.OccurredAt));
    }
}

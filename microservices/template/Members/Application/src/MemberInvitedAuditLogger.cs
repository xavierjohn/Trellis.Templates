using Microsoft.Extensions.Logging;
using ProjectTrackerTemplate.Members.Domain;
using Trellis.Mediator;

namespace ProjectTrackerTemplate.Members.Application;

// Post-commit business-event log. This is the correct home for the "member invited" audit line that
// the invite command deliberately does NOT emit inline: the aggregate raises MemberInvited, the
// outbox captures it in the same transaction as the member row, and the relay dispatches it to this
// handler AFTER the commit — so a commit failure can never produce a success log for a member that
// was never persisted. Logs the member + tenant identity and role only, never the email (PII).
internal sealed partial class MemberInvitedAuditLogger(ILogger<MemberInvitedAuditLogger> logger) : IDomainEventHandler<MemberInvited>
{
    public ValueTask HandleAsync(MemberInvited domainEvent, CancellationToken cancellationToken)
    {
        LogMemberInvited(logger, domainEvent.MemberId.Value, domainEvent.TenantId.Value, domainEvent.Role.Value, domainEvent.OccurredAt);
        return ValueTask.CompletedTask;
    }

    [LoggerMessage(1, LogLevel.Information, "Member {MemberId} invited to tenant {TenantId} as {Role} at {OccurredAt}")]
    private static partial void LogMemberInvited(ILogger logger, string memberId, string tenantId, string role, DateTimeOffset occurredAt);
}

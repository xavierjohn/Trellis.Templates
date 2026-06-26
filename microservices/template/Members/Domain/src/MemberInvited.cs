using Trellis;

namespace ProjectTrackerTemplate.Members.Domain;

// Domain event — INTERNAL to the Members bounded context. Raised by the Member aggregate when an
// invitation happens (Member.Invite). The outbox captures it in the same transaction as the member
// row; the relay then dispatches it after the commit to its handlers:
//   * MemberInvitedAuditLogger   — writes the post-commit business-event log.
//   * MemberInvitedTranslator    — translates it into the external MemberInvitedIntegrationEvent.
//
// It carries the member + tenant identity and role, but NOT the email: PII stays out of the event
// stream. OccurredAt is stamped from the injected TimeProvider so the time is testable.
public sealed record MemberInvited(TenantId TenantId, MemberId MemberId, string Role, DateTimeOffset OccurredAt) : IDomainEvent;

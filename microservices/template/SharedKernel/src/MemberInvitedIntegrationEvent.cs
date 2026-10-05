using Trellis;

namespace ProjectTrackerTemplate.SharedKernel;

// PUBLISHED LANGUAGE (Evans' context-mapping pattern) — distinct from the Shared Kernel proper.
//
// The Shared Kernel (TenantId) is a domain concept both contexts co-own and use INTERNALLY. An
// integration event is the stable, versioned CONTRACT a context publishes for others to consume.
// They live in the same shared project for the template's sake, but they are different patterns:
// changing TenantId is a co-owned domain decision; changing this contract is a publish/subscribe
// compatibility decision (add fields, never repurpose them; rev the wire name on a breaking change).
//
// Members publishes MemberInvited when a member is invited; Projects consumes it to maintain a local
// "team directory" read model. The contract is deliberately made of PRIMITIVES, not value objects:
// the wire format is a boundary, so consumers bind to plain strings and never couple to Members'
// internal MemberId type. TenantId stays a string here too (each service re-creates its own TenantId
// value object from it on the way in).
//
// Transport identity belongs to the outbox envelope, not the payload. The inbox deduplicates retries
// of the same row; the projection separately deduplicates re-translated rows by tenant + member.
[IntegrationEventName("projecttracker.members.member-invited.v2")]
public sealed record MemberInvitedIntegrationEvent(
    string TenantId,
    string MemberId,
    string Role,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
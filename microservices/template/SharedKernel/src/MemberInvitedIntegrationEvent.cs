using Trellis;

namespace ProjectTrackerTemplate.SharedKernel;

// PUBLISHED LANGUAGE (Evans' context-mapping pattern) — distinct from the Shared Kernel proper.
//
// The Shared Kernel (TenantId) is a domain concept both contexts co-own and use INTERNALLY. An
// integration event is the stable, versioned CONTRACT a context publishes for others to consume.
// They live in the same shared project for the template's sake, but they are different patterns:
// changing TenantId is a co-owned domain decision; changing this contract is a publish/subscribe
// compatibility decision (add fields, never repurpose them; rev the MessageType on a breaking change).
//
// Members publishes MemberInvited when a member is invited; Projects consumes it to maintain a local
// "team directory" read model. The contract is deliberately made of PRIMITIVES, not value objects:
// the wire format is a boundary, so consumers bind to plain strings and never couple to Members'
// internal MemberId type. TenantId stays a string here too (each service re-creates its own TenantId
// value object from it on the way in).
//
// EventId is the dedup identity. Trellis' outbox is at-least-once and re-runs a translator on retry,
// so the same invitation can be published more than once with a different outbox row id each time
// (see trellis-api-efcore-outbox.md: "dedupe on business identity, not the message id"). The producer
// derives EventId DETERMINISTICALLY from the member's business key, so every copy of one invitation
// carries the same EventId and the consumer's inbox collapses them to a single effect.
public sealed record MemberInvitedIntegrationEvent(
    Guid EventId,
    string TenantId,
    string MemberId,
    string Role,
    DateTimeOffset OccurredAt) : IIntegrationEvent
{
    // Stable wire discriminator the transport stamps on each message so the consumer knows what to
    // deserialize. It is decoupled from the CLR type name on purpose — rename the record freely, but
    // bump the trailing version here (and add a parallel handler) for a breaking schema change.
    public const string MessageType = "projecttracker.members.member-invited.v1";
}

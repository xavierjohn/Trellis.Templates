using ProjectTrackerTemplate.Members.Domain;
using Trellis.Mediator;

namespace ProjectTrackerTemplate.Members.Application;

// Translator (Evans' published-language seam): turns the INTERNAL MemberInvited domain event into the
// EXTERNAL MemberInvitedIntegrationEvent contract other services consume. It is an ordinary domain-
// event handler; while the outbox relay re-dispatches the captured domain event, whatever it Add()s
// to the collector the relay enrolls as a durable integration row and later hands to the configured
// IIntegrationEventPublisher (the Service Bus adapter, in this template).
//
// The EventId is derived deterministically from the member's business key, so a redelivered or
// re-translated invitation always carries one identity and the consumer's inbox dedupes on it. The
// email is deliberately dropped: the external contract carries no PII.
internal sealed class MemberInvitedTranslator(IIntegrationEventCollector collector) : IDomainEventHandler<MemberInvited>
{
    public ValueTask HandleAsync(MemberInvited domainEvent, CancellationToken cancellationToken)
    {
        collector.Add(new MemberInvitedIntegrationEvent(
            DeterministicEventId.ForMember(domainEvent.MemberId),
            domainEvent.TenantId,
            domainEvent.MemberId,
            domainEvent.Role,
            domainEvent.OccurredAt));

        return ValueTask.CompletedTask;
    }
}

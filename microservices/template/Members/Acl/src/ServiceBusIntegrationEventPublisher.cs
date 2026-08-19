using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Trellis.Mediator;

namespace ProjectTrackerTemplate.Members.Acl;

// Transport adapter that replaces Trellis' default in-process IIntegrationEventPublisher so the outbox
// relay delivers integration events to OTHER services over Azure Service Bus. The producing side —
// aggregates, the translator, the outbox — is identical to a modular monolith; only this registration
// changes between in-process fan-out and a broker. That is the seam that keeps the outbox transport-
// agnostic.
//
// The relay treats a throw as a RETRYABLE failure (it records the error, backs off, and redelivers on
// a later drain up to the configured attempt cap). So a transient Service Bus outage must PROPAGATE
// out of PublishAsync rather than be swallowed — that is what makes delivery durable and at-least-once.
internal sealed class ServiceBusIntegrationEventPublisher : IIntegrationEventPublisher, IAsyncDisposable
{
    private readonly ServiceBusSender _sender;

    public ServiceBusIntegrationEventPublisher(ServiceBusClient client) =>
        _sender = client.CreateSender(MemberEventsChannel.QueueName);

    public async ValueTask PublishAsync(OutboundIntegrationMessage message, CancellationToken cancellationToken)
    {
        // This service publishes exactly one contract. Fail fast on anything else: because this adapter
        // REPLACES the in-process publisher globally, silently returning would let the outbox mark the row
        // processed and drop the event. A throw is recorded as a relay failure (retried, then parked with
        // the error visible) so a missing mapping surfaces instead of vanishing.
        if (message.Event is not MemberInvitedIntegrationEvent invited)
            throw new NotSupportedException(
                $"No Service Bus mapping for integration event '{message.Event.GetType().Name}'. " +
                "Add one here when this service starts publishing a new contract.");

        var json = JsonSerializer.Serialize(invited, IntegrationEventSerialization.Options);
        var busMessage = new ServiceBusMessage(json)
        {
            Subject = MemberInvitedIntegrationEvent.MessageType,
            ContentType = "application/json",
            // Deliberately the deterministic EventId, NOT message.MessageId (the outbox row id).
            //
            // Trellis' default guidance is to stamp message.MessageId verbatim, because a consumer that
            // dedupes on the transport id needs redeliveries of one row to share an id. This template
            // dedupes on a different, stronger key: MemberEventsConsumer builds its IntegrationEnvelope
            // from the payload's EventId, which DeterministicEventId derives from the business key.
            //
            // That distinction matters because the outbox produces two classes of duplicate. Redelivering
            // one row repeats the row id AND the EventId, so either key collapses it. But a retried domain
            // row re-runs the translator and stages a genuinely NEW outbox row — a new row id carrying the
            // same business event. Only the deterministic EventId collapses that second class, so it is
            // the identity that travels on the wire and the one Service Bus native duplicate detection
            // sees. Switching this to message.MessageId would silently reintroduce duplicate invitations.
            MessageId = invited.EventId.ToString("N"),
        };

        await _sender.SendMessageAsync(busMessage, cancellationToken);
    }

    public ValueTask DisposeAsync() => _sender.DisposeAsync();
}

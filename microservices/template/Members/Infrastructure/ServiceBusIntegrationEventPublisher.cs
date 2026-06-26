using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Trellis;
using Trellis.Mediator;

namespace ProjectTrackerTemplate.Members.Infrastructure;

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

    public async ValueTask PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        // This service publishes exactly one contract; serialize by its concrete type so the wire
        // payload is the contract shape, not the IIntegrationEvent interface.
        if (integrationEvent is not MemberInvitedIntegrationEvent invited)
            return;

        var json = JsonSerializer.Serialize(invited, IntegrationEventSerialization.Options);
        var message = new ServiceBusMessage(json)
        {
            Subject = MemberInvitedIntegrationEvent.MessageType,
            ContentType = "application/json",
            // Carry the dedup identity as the broker-native MessageId too — a best-effort first line of
            // defence (Service Bus duplicate detection, when enabled) ahead of the inbox's authoritative
            // (ConsumerId, MessageId) guard on the consumer.
            MessageId = invited.EventId.ToString("N"),
        };

        await _sender.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => _sender.DisposeAsync();
}

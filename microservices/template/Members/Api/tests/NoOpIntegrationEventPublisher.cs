using Trellis.Mediator;

namespace Members.Api.Tests;

// Hermetic stand-in for the Service Bus publisher: in-memory tests don't deliver integration events to a
// real broker. The cross-service eventing flow is covered separately by the in-memory-broker integration
// test; here the relay drains the outbox into this no-op so an invite commits without a broker.
internal sealed class NoOpIntegrationEventPublisher : IIntegrationEventPublisher
{
    public ValueTask PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}

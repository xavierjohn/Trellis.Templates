using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using ProjectTrackerTemplate.SharedKernel;
using Trellis.Mediator;

namespace Eventing.Tests;

// A minimal in-process stand-in for the Service Bus queue: the Members publisher enqueues a serialized
// integration event and the Projects consumer dequeues it into the inbox dispatcher. Shared by both hosts
// in a single test process, it lets the cross-service eventing flow run with no broker container.
public sealed class InMemoryBroker
{
    private readonly Channel<byte[]> _channel = Channel.CreateUnbounded<byte[]>();

    public ValueTask PublishAsync(byte[] message, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(message, cancellationToken);

    public IAsyncEnumerable<byte[]> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

// Members side: replaces ServiceBusIntegrationEventPublisher. The outbox relay drains captured events into
// this, which serializes each onto the broker exactly as the real adapter serializes onto Service Bus.
internal sealed class InMemoryBrokerPublisher(InMemoryBroker broker) : IIntegrationEventPublisher
{
    public async ValueTask PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent is not MemberInvitedIntegrationEvent invited)
            return;

        var bytes = JsonSerializer.SerializeToUtf8Bytes(invited, IntegrationEventSerialization.Options);
        await broker.PublishAsync(bytes, cancellationToken).ConfigureAwait(false);
    }
}

// Projects side: replaces MemberEventsConsumer. Reads the broker and feeds each message to the inbox
// dispatcher inside an IntegrationEnvelope keyed on the producer's deterministic EventId — the same dedup
// contract the real Service Bus pump uses, so the inbox collapses redeliveries to one effect.
internal sealed class InMemoryBrokerConsumer(InMemoryBroker broker, IInboxDispatcher inbox) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in broker.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            var integrationEvent = JsonSerializer.Deserialize<MemberInvitedIntegrationEvent>(
                message, IntegrationEventSerialization.Options);
            if (integrationEvent is null)
                continue;

            var envelope = new IntegrationEnvelope(integrationEvent.EventId, integrationEvent) { MessageSource = "members" };
            await inbox.DispatchAsync(envelope, stoppingToken).ConfigureAwait(false);
        }
    }
}

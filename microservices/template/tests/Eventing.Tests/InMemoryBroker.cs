using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Azure.Messaging.ServiceBus;

namespace Eventing.Tests;

// Only the Azure SDK boundary is substituted. The real Trellis publisher, wire format, consumer,
// settlement and inbox all run, without requiring a broker container.
public sealed class InMemoryBroker
{
    private readonly Channel<ServiceBusReceivedMessage> _channel = Channel.CreateUnbounded<ServiceBusReceivedMessage>();

    public ConcurrentQueue<ServiceBusReceivedMessage> Completed { get; } = new();
    public string? Topic { get; internal set; }
    public string? Subscription { get; internal set; }

    public ValueTask PublishAsync(ServiceBusMessage message, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: message.Body,
            messageId: message.MessageId,
            subject: message.Subject,
            contentType: message.ContentType,
            correlationId: message.CorrelationId,
            properties: new Dictionary<string, object>(message.ApplicationProperties)), cancellationToken);

    public IAsyncEnumerable<ServiceBusReceivedMessage> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

internal sealed class InMemoryServiceBusClient(InMemoryBroker broker) : ServiceBusClient
{
    public override ServiceBusSender CreateSender(string queueOrTopicName) => new InMemorySender(broker);

    public override ServiceBusProcessor CreateProcessor(
        string topicName, string subscriptionName, ServiceBusProcessorOptions options)
    {
        broker.Topic = topicName;
        broker.Subscription = subscriptionName;
        return new InMemoryProcessor(broker);
    }

    [SuppressMessage("Usage", "CA2215", Justification = "The SDK test-double constructor creates no transport to dispose.")]
    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class InMemorySender(InMemoryBroker broker) : ServiceBusSender
{
    public override Task SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken = default) =>
        broker.PublishAsync(message, cancellationToken).AsTask();

    [SuppressMessage("Usage", "CA2215", Justification = "The SDK test-double constructor creates no transport to dispose.")]
    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class InMemoryProcessor(InMemoryBroker broker) : ServiceBusProcessor
{
    private CancellationTokenSource? _stop;
    private Task? _pump;

    public override Task StartProcessingAsync(CancellationToken cancellationToken = default)
    {
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pump = PumpAsync(_stop.Token);
        return Task.CompletedTask;
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        await foreach (var message in broker.ReadAllAsync(cancellationToken))
            await OnProcessMessageAsync(new ProcessMessageEventArgs(
                message, new InMemoryReceiver(broker), "in-memory", cancellationToken));
    }

    public override async Task StopProcessingAsync(CancellationToken cancellationToken = default)
    {
        if (_stop is null || _pump is null)
            return;

        await _stop.CancelAsync();
        try
        {
            await _pump.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Expected receive-loop cancellation; dispatch faults still propagate.
        }
    }

    public override async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        await StopProcessingAsync(cancellationToken);
        _stop?.Dispose();
    }
}

internal sealed class InMemoryReceiver(InMemoryBroker broker) : ServiceBusReceiver
{
    public override Task CompleteMessageAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken = default)
    {
        broker.Completed.Enqueue(message);
        return Task.CompletedTask;
    }
}
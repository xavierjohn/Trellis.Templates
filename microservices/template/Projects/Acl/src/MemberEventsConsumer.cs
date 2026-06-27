using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trellis.Mediator;

namespace ProjectTrackerTemplate.Projects.Acl;

// Transport pump (application-owned): receives messages from the Service Bus queue and feeds each into
// the inbox dispatcher. Trellis ships the inbox (dedup + atomic handler commit) but NOT the broker
// glue — receiving from a specific transport is the app's job. Swapping Service Bus for Kafka or
// RabbitMQ replaces only this class; the inbox, handlers, and read model do not change.
internal sealed partial class MemberEventsConsumer : BackgroundService
{
    private readonly ServiceBusClient _client;
    private readonly IInboxDispatcher _inbox;
    private readonly ILogger<MemberEventsConsumer> _logger;
    private ServiceBusProcessor? _processor;

    public MemberEventsConsumer(ServiceBusClient client, IInboxDispatcher inbox, ILogger<MemberEventsConsumer> logger)
    {
        _client = client;
        _inbox = inbox;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = _client.CreateProcessor(
            MemberEventsChannel.QueueName,
            new ServiceBusProcessorOptions { AutoCompleteMessages = false, MaxConcurrentCalls = 1 });

        _processor.ProcessMessageAsync += OnMessageAsync;
        _processor.ProcessErrorAsync += OnErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken);

        // Keep the background service alive for the app's lifetime. The processor pumps messages on its
        // own callbacks; without this await ExecuteAsync would complete immediately and the host would
        // report the consumer as stopped. StopAsync stops + disposes the processor on shutdown.
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown — the host signalled stoppingToken.
        }
    }

    private async Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        var message = args.Message;

        // The wire discriminator tells us which contract this is. We only understand MemberInvited;
        // anything else is dead-lettered (not abandoned) so it does not loop forever on this subscription.
        if (message.Subject != MemberInvitedIntegrationEvent.MessageType)
        {
            await args.DeadLetterMessageAsync(message, "unknown-subject", $"No handler for subject '{message.Subject}'.", args.CancellationToken);
            return;
        }

        MemberInvitedIntegrationEvent? evt;
        try
        {
            evt = JsonSerializer.Deserialize<MemberInvitedIntegrationEvent>(message.Body.ToString(), IntegrationEventSerialization.Options);
        }
        catch (JsonException ex)
        {
            LogMalformedPayload(_logger, message.MessageId, message.Subject, ex);
            evt = null;
        }

        if (evt is null)
        {
            await args.DeadLetterMessageAsync(message, "malformed-payload", "Body is not a MemberInvitedIntegrationEvent.", args.CancellationToken);
            return;
        }

        try
        {
            // The envelope MessageId is the producer's deterministic EventId — the inbox dedup key. A
            // redelivery (or a re-translated copy) carries the same id, so DispatchAsync collapses it to
            // one effect. The lineage fields are observability only and never participate in dedup.
            var envelope = new IntegrationEnvelope(evt.EventId, evt) { MessageSource = "members" };
            await _inbox.DispatchAsync(envelope, args.CancellationToken);
            await args.CompleteMessageAsync(message, args.CancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A handler or infrastructure failure: abandon so Service Bus redelivers. The inbox makes the
            // retry safe — nothing was committed, so re-running produces no partial effect.
            LogDispatchFailed(_logger, evt.MemberId, ex);
            await args.AbandonMessageAsync(message, cancellationToken: args.CancellationToken);
        }
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        LogProcessorError(_logger, args.ErrorSource.ToString(), args.Exception);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }

    [LoggerMessage(1, LogLevel.Error, "Failed to dispatch MemberInvited for member {MemberId}; abandoning for redelivery.")]
    private static partial void LogDispatchFailed(ILogger logger, string memberId, Exception exception);

    [LoggerMessage(2, LogLevel.Error, "Service Bus processor error from {ErrorSource}.")]
    private static partial void LogProcessorError(ILogger logger, string errorSource, Exception exception);

    [LoggerMessage(3, LogLevel.Error, "Dead-lettering malformed message {MessageId} (subject {Subject}); body is not a MemberInvitedIntegrationEvent.")]
    private static partial void LogMalformedPayload(ILogger logger, string messageId, string? subject, Exception exception);
}

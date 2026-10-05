using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ProjectTrackerTemplate.Members.Acl;
using ProjectTrackerTemplate.SharedKernel;
using Trellis.Mediator;

namespace Members.Acl.Tests;

public class ServiceBusPublisherTests
{
    [Fact]
    public async Task Publisher_preserves_the_outbox_identity_and_uses_the_contract_topic()
    {
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:membersdb"] = "Server=(localdb)\\MSSQLLocalDB;Database=members-test",
            ["ConnectionStrings:messaging"] = "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true",
        });
        builder.AddMembersAcl();
        var client = new RecordingClient();
        builder.Services.Replace(ServiceDescriptor.Singleton<ServiceBusClient>(client));
        await using var provider = builder.Services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IIntegrationEventPublisher>();
        publisher.Should().BeOfType<Trellis.Messaging.AzureServiceBus.ServiceBusIntegrationEventPublisher>();

        var evt = new MemberInvitedIntegrationEvent(
            "acme", "acme-alice", "owner", DateTimeOffset.UtcNow);
        var outbound = new OutboundIntegrationMessage(Guid.CreateVersion7(), evt);
        await publisher.PublishAsync(outbound, TestContext.Current.CancellationToken);
        await publisher.PublishAsync(outbound, TestContext.Current.CancellationToken);

        client.Topic.Should().Be("projecttracker.members.member-invited.v2");
        client.Sender.Messages.Should().HaveCount(2);
        foreach (var message in client.Sender.Messages)
        {
            Guid.Parse(message.MessageId).Should().Be(outbound.MessageId);
            message.Subject.Should().Be(client.Topic);
            message.ContentType.Should().Be("application/json");
            JsonSerializer.Deserialize<MemberInvitedIntegrationEvent>(message.Body.ToString(), JsonSerializerOptions.Web)
                .Should().Be(evt);
        }
    }

    private sealed class RecordingClient : ServiceBusClient
    {
        public string? Topic { get; private set; }
        public RecordingSender Sender { get; } = new();

        public override ServiceBusSender CreateSender(string queueOrTopicName)
        {
            Topic = queueOrTopicName;
            return Sender;
        }

        [SuppressMessage("Usage", "CA2215", Justification = "The SDK test-double constructor creates no transport to dispose.")]
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingSender : ServiceBusSender
    {
        public List<ServiceBusMessage> Messages { get; } = [];

        public override Task SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }

        [SuppressMessage("Usage", "CA2215", Justification = "The SDK test-double constructor creates no transport to dispose.")]
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
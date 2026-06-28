using ProjectTrackerTemplate.Members.Application;
using ProjectTrackerTemplate.Members.Domain;
using ProjectTrackerTemplate.SharedKernel;
using Trellis.Mediator;

namespace Members.Application.Tests;

public class MemberInvitedTranslatorTests
{
    [Fact]
    public async Task Translates_to_the_integration_contract_with_a_deterministic_id()
    {
        var collector = new RecordingCollector();
        var translator = new MemberInvitedTranslator(collector);
        var tenant = TenantId.TryCreate("acme").GetValueOrThrow("valid tenant");
        var id = MemberId.TryCreate("acme-alice").GetValueOrThrow("valid id");
        var domainEvent = new MemberInvited(tenant, id, Role.Owner, DateTimeOffset.UtcNow);

        await translator.HandleAsync(domainEvent, CancellationToken.None);

        var published = collector.Added.Should().ContainSingle()
            .Which.Should().BeOfType<MemberInvitedIntegrationEvent>().Subject;
        published.TenantId.Should().Be("acme");
        published.MemberId.Should().Be("acme-alice");
        published.Role.Should().Be("owner");
        published.EventId.Should().Be(DeterministicEventId.ForMember("acme-alice"));
    }

    private sealed class RecordingCollector : IIntegrationEventCollector
    {
        public List<IIntegrationEvent> Added { get; } = [];

        public void Add(IIntegrationEvent integrationEvent) => Added.Add(integrationEvent);

        public IReadOnlyList<IIntegrationEvent> DrainPending()
        {
            var drained = Added.ToList();
            Added.Clear();
            return drained;
        }
    }
}

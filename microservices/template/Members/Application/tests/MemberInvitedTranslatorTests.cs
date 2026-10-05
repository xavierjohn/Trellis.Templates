using ProjectTrackerTemplate.Members.Application;
using ProjectTrackerTemplate.Members.Domain;
using ProjectTrackerTemplate.SharedKernel;
using Trellis.Mediator;

namespace Members.Application.Tests;

public class MemberInvitedTranslatorTests
{
    [Fact]
    public async Task Translates_to_the_integration_contract_without_exposing_email()
    {
        var collector = new RecordingCollector();
        var translator = new MemberInvitedTranslator(collector);
        var tenant = TenantId.TryCreate("acme").GetValueOrThrow("valid tenant");
        var id = MemberId.TryCreate("acme-alice").GetValueOrThrow("valid id");
        var domainEvent = new MemberInvited(tenant, id, Role.Owner, DateTimeOffset.UtcNow);

        using var translation = collector.BeginTranslation();
        await translator.HandleAsync(domainEvent, CancellationToken.None);

        var published = collector.Added.Should().ContainSingle()
            .Which.Should().BeOfType<MemberInvitedIntegrationEvent>().Subject;
        published.TenantId.Should().Be("acme");
        published.MemberId.Should().Be("acme-alice");
        published.Role.Should().Be("owner");
        published.OccurredAt.Should().Be(domainEvent.OccurredAt);
    }

    private sealed class RecordingCollector : IIntegrationEventCollector
    {
        private bool _translationActive;

        public List<IIntegrationEvent> Added { get; } = [];

        public IDisposable BeginTranslation()
        {
            if (_translationActive)
                throw new InvalidOperationException("A translation is already active.");

            _translationActive = true;
            return new TranslationLease(this);
        }

        public void Add(IIntegrationEvent integrationEvent)
        {
            ArgumentNullException.ThrowIfNull(integrationEvent);
            if (!_translationActive)
                throw new InvalidOperationException("Integration events require an active translation.");

            Added.Add(integrationEvent);
        }

        public IReadOnlyList<IIntegrationEvent> DrainPending()
        {
            if (!_translationActive)
                throw new InvalidOperationException("Integration events require an active translation.");

            var drained = Added.ToList();
            Added.Clear();
            return drained;
        }

        private sealed class TranslationLease(RecordingCollector collector) : IDisposable
        {
            private RecordingCollector? _collector = collector;

            public void Dispose()
            {
                if (_collector is not { } activeCollector)
                    return;

                activeCollector.Added.Clear();
                activeCollector._translationActive = false;
                _collector = null;
            }
        }
    }
}
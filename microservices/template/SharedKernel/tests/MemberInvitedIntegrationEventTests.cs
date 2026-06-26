using System.Text.Json;
using ProjectTrackerTemplate.SharedKernel;

namespace SharedKernel.Tests;

public class MemberInvitedIntegrationEventTests
{
    [Fact]
    public void Round_trips_through_the_shared_serialization_options()
    {
        var evt = new MemberInvitedIntegrationEvent(
            Guid.NewGuid(), "acme", "acme-alice", "owner", DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(evt, IntegrationEventSerialization.Options);
        var back = JsonSerializer.Deserialize<MemberInvitedIntegrationEvent>(json, IntegrationEventSerialization.Options);

        back.Should().Be(evt);
    }
}

using System.Text.Json;
using ProjectTrackerTemplate.SharedKernel;

namespace SharedKernel.Tests;

public class MemberInvitedIntegrationEventTests
{
    [Fact]
    public void Round_trips_through_the_framework_Web_serialization_defaults()
    {
        var evt = new MemberInvitedIntegrationEvent(
            "acme", "acme-alice", "owner", DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(evt, JsonSerializerOptions.Web);
        var back = JsonSerializer.Deserialize<MemberInvitedIntegrationEvent>(json, JsonSerializerOptions.Web);

        back.Should().Be(evt);
    }
}
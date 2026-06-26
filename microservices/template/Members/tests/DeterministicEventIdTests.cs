using ProjectTrackerTemplate.Members.Infrastructure;

namespace Members.Tests;

public class DeterministicEventIdTests
{
    [Fact]
    public void ForMember_is_deterministic()
        => DeterministicEventId.ForMember("acme-alice")
            .Should().Be(DeterministicEventId.ForMember("acme-alice"));

    [Fact]
    public void ForMember_distinguishes_members()
        => DeterministicEventId.ForMember("acme-alice")
            .Should().NotBe(DeterministicEventId.ForMember("acme-bob"));

    [Fact]
    public void ForMember_produces_a_version_8_uuid()
        => DeterministicEventId.ForMember("acme-alice").Version.Should().Be(8);
}

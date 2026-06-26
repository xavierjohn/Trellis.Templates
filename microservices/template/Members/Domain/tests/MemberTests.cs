using ProjectTrackerTemplate.Members.Domain;
using ProjectTrackerTemplate.SharedKernel;

namespace Members.Domain.Tests;

public class MemberTests
{
    [Fact]
    public void Invite_raises_MemberInvited_with_the_member_details()
    {
        var id = MemberId.TryCreate("acme-alice").GetValueOrThrow("valid id");
        var tenant = TenantId.TryCreate("acme").GetValueOrThrow("valid tenant");
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var member = Member.Invite(id, tenant, "alice@acme.example", "owner", new FixedTime(now));

        var raised = member.UncommittedEvents().Should().ContainSingle()
            .Which.Should().BeOfType<MemberInvited>().Subject;
        raised.TenantId.Should().Be(tenant);
        raised.MemberId.Should().Be(id);
        raised.Role.Should().Be("owner");
        raised.OccurredAt.Should().Be(now);
    }

    [Fact]
    public void Plain_constructor_raises_no_event()
    {
        var id = MemberId.TryCreate("acme-bob").GetValueOrThrow("valid id");
        var tenant = TenantId.TryCreate("acme").GetValueOrThrow("valid tenant");

        var member = new Member(id, tenant, "bob@acme.example", "contributor");

        member.UncommittedEvents().Should().BeEmpty();
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

using ProjectTrackerTemplate.Members.Domain;
using ProjectTrackerTemplate.SharedKernel;
using Trellis.Primitives;

namespace Members.Domain.Tests;

public class MemberTests
{
    [Fact]
    public void Invite_raises_MemberInvited_with_the_member_details()
    {
        var id = MemberId.TryCreate("acme-alice").GetValueOrThrow("valid id");
        var tenant = TenantId.TryCreate("acme").GetValueOrThrow("valid tenant");
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var member = Member.Invite(id, tenant, Email("alice@acme.example"), Role.Owner, new FixedTime(now));

        var raised = member.UncommittedEvents().Should().ContainSingle()
            .Which.Should().BeOfType<MemberInvited>().Subject;
        raised.TenantId.Should().Be(tenant);
        raised.MemberId.Should().Be(id);
        raised.Role.Should().Be(Role.Owner);
        raised.OccurredAt.Should().Be(now);
    }

    [Fact]
    public void Plain_constructor_raises_no_event()
    {
        var id = MemberId.TryCreate("acme-bob").GetValueOrThrow("valid id");
        var tenant = TenantId.TryCreate("acme").GetValueOrThrow("valid tenant");

        var member = new Member(id, tenant, Email("bob@acme.example"), Role.Contributor);

        member.UncommittedEvents().Should().BeEmpty();
    }

    private static EmailAddress Email(string value) =>
        EmailAddress.TryCreate(value).GetValueOrThrow("valid email");

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

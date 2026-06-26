using ProjectTrackerTemplate.Projects.Api;
using ProjectTrackerTemplate.Projects.ReadModel;
using ProjectTrackerTemplate.SharedKernel;

namespace Projects.Api.Tests;

// The wire-format projection GET /api/team returns. Projects the read model's TenantId value object to
// its string on the way out (mirrors the producer's external contract).
public class TeamMemberResponseTests
{
    [Fact]
    public void From_projects_the_read_model_onto_the_response_contract()
    {
        var invitedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var known = new KnownMember(
            TenantId.TryCreate("acme").GetValueOrThrow("valid tenant"),
            "acme-alice",
            "owner",
            invitedAt);

        var response = TeamMemberResponse.From(known);

        response.MemberId.Should().Be("acme-alice");
        response.TenantId.Should().Be("acme");
        response.Role.Should().Be("owner");
        response.InvitedAt.Should().Be(invitedAt);
    }
}

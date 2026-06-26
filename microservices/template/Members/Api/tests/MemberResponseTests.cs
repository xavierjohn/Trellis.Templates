using ProjectTrackerTemplate.Members.Api;
using ProjectTrackerTemplate.Members.Domain;
using ProjectTrackerTemplate.SharedKernel;

namespace Members.Api.Tests;

// The wire-format projection the GET endpoint returns. Unlike the cross-service integration event (which
// drops the email as PII), the API response to the owning tenant carries it; this pins that contract.
public class MemberResponseTests
{
    [Fact]
    public void From_projects_the_member_onto_the_response_contract()
    {
        var member = new Member(
            MemberId.TryCreate("acme-alice").GetValueOrThrow("valid id"),
            TenantId.TryCreate("acme").GetValueOrThrow("valid tenant"),
            "alice@acme.example",
            "owner");

        var response = MemberResponse.From(member);

        response.Id.Should().Be("acme-alice");
        response.TenantId.Should().Be("acme");
        response.Email.Should().Be("alice@acme.example");
        response.Role.Should().Be("owner");
    }
}

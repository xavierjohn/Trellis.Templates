using ProjectTrackerTemplate.Members.Application;
using ProjectTrackerTemplate.Members.Domain;
using Trellis.Primitives;

namespace Members.Application.Tests;

public class InviteMemberCommandTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TryCreate_reports_all_missing_fields_or_preserves_valid_values(bool missingEmail, bool missingRole)
    {
        var email = missingEmail ? null : EmailAddress.Create("alice@example.com");
        var role = missingRole ? null : Role.Owner;
        var expected = new List<FieldViolation>();
        if (missingEmail)
            expected.Add(new FieldViolation(InputPointer.ForProperty("email"), ValidationCodes.ValueNotNull, Detail: "Email is required."));
        if (missingRole)
            expected.Add(new FieldViolation(InputPointer.ForProperty("role"), ValidationCodes.ValueNotNull, Detail: "Role is required."));

        var result = InviteMemberCommand.TryCreate(email, role);

        if (expected.Count > 0)
        {
            result.Should().BeFailureOfType<Error.InvalidInput>().Which.Fields.Items.Should().Equal(expected);
            return;
        }

        var command = result.Should().BeSuccess().Which;
        command.Email.Should().BeSameAs(email);
        command.Role.Should().BeSameAs(role);
    }
}
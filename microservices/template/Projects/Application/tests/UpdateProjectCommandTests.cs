using ProjectTrackerTemplate.Projects.Application;
using ProjectTrackerTemplate.Projects.Domain;

namespace Projects.Application.Tests;

public class UpdateProjectCommandTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void TryCreate_reports_all_missing_fields_or_preserves_valid_values(bool missingId, bool missingTitle, bool missingDescription)
    {
        var id = missingId ? null : ProjectId.Create("acme-project");
        var title = missingTitle ? null : ProjectTitle.Create("Test");
        var description = missingDescription ? null : ProjectDescription.Create("Description");
        EntityTagValue[] etags = [EntityTagValue.Strong("v1")];
        var expected = new List<FieldViolation>();
        if (missingId)
            expected.Add(new FieldViolation(InputPointer.ForProperty("id"), ValidationCodes.ValueNotNull, Detail: "Project id is required."));
        if (missingTitle)
            expected.Add(new FieldViolation(InputPointer.ForProperty("title"), ValidationCodes.ValueNotNull, Detail: "Title is required."));
        if (missingDescription)
            expected.Add(new FieldViolation(InputPointer.ForProperty("description"), ValidationCodes.ValueNotNull, Detail: "Description is required."));

        var result = UpdateProjectCommand.TryCreate(id, title, description, etags);

        if (expected.Count > 0)
        {
            result.Should().BeFailureOfType<Error.InvalidInput>().Which.Fields.Items.Should().Equal(expected);
            return;
        }

        var command = result.Should().BeSuccess().Which;
        command.Id.Should().BeSameAs(id);
        command.Title.Should().BeSameAs(title);
        command.Description.Should().BeSameAs(description);
        command.IfMatchETags.Should().BeSameAs(etags);
    }
}
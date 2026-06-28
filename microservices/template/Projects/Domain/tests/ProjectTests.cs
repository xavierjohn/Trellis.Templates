using ProjectTrackerTemplate.Projects.Domain;
using ProjectTrackerTemplate.SharedKernel;

namespace Projects.Domain.Tests;

// The Project aggregate is loaded once by the resource-auth pipeline, then mutated + re-read by the
// handler. Update mutates in place — the behaviour that proves the v4 accessor reads the same instance.
public class ProjectTests
{
    [Fact]
    public void Update_replaces_the_title_and_description()
    {
        var project = new Project(
            ProjectId.TryCreate("acme-p1").GetValueOrThrow("valid id"),
            ownerId: "alice",
            tenantId: TenantId.TryCreate("acme").GetValueOrThrow("valid tenant"),
            title: Title("Q1 launch"),
            description: Description("Coordinate Q1 marketing launch."));

        project.Update(Title("Q2 launch"), Description("Coordinate Q2 launch."));

        project.Title.Value.Should().Be("Q2 launch");
        project.Description.Value.Should().Be("Coordinate Q2 launch.");
    }

    private static ProjectTitle Title(string value) =>
        ProjectTitle.TryCreate(value).GetValueOrThrow("valid title");

    private static ProjectDescription Description(string value) =>
        ProjectDescription.TryCreate(value).GetValueOrThrow("valid description");
}

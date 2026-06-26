using ProjectTrackerTemplate.Projects.Domain;
using ProjectTrackerTemplate.SharedKernel;

namespace Projects.Domain.Tests;

// The Project aggregate is the in-memory starter the auth walkthrough loads, mutates, and re-reads.
// Update mutates in place — the behaviour that proves the v4 accessor reads the same instance.
public class ProjectTests
{
    [Fact]
    public void Update_replaces_the_title_and_description()
    {
        var project = new Project(
            ProjectId.TryCreate("acme-p1").GetValueOrThrow("valid id"),
            ownerId: "alice",
            tenantId: TenantId.TryCreate("acme").GetValueOrThrow("valid tenant"),
            title: "Q1 launch",
            description: "Coordinate Q1 marketing launch.");

        project.Update("Q2 launch", "Coordinate Q2 launch.");

        project.Title.Should().Be("Q2 launch");
        project.Description.Should().Be("Coordinate Q2 launch.");
    }
}

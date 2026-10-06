#pragma warning disable IDE0047
using Microsoft.EntityFrameworkCore;
using ProjectTrackerTemplate.Projects.Acl;

namespace Projects.Acl.Tests;

public sealed class SelectedProviderSchemaTests
{
    [Fact]
    public void Selected_provider_can_generate_the_aggregate_read_model_and_inbox_schema()
    {
        var options = new DbContextOptionsBuilder<ProjectsDbContext>();
#if (UsePostgres)
        options.UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused");
        const string provider = "Npgsql.EntityFrameworkCore.PostgreSQL";
#else
        options.UseSqlServer("Server=localhost;Database=unused;Integrated Security=True");
        const string provider = "Microsoft.EntityFrameworkCore.SqlServer";
#endif
        using var context = new ProjectsDbContext(options.Options);

        context.Database.ProviderName.Should().Be(provider);
        var script = context.Database.GenerateCreateScript();
        script.Should().Contain("CREATE TABLE").And.Contain("Projects").And.Contain("KnownMembers")
            .And.Contain("TrellisInboxMessages");
    }
}
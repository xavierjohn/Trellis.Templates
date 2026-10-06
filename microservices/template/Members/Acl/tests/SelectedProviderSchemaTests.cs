#pragma warning disable IDE0047
using Microsoft.EntityFrameworkCore;
using ProjectTrackerTemplate.Members.Acl;

namespace Members.Acl.Tests;

public sealed class SelectedProviderSchemaTests
{
    [Fact]
    public void Selected_provider_can_generate_the_aggregate_and_outbox_schema()
    {
        var options = new DbContextOptionsBuilder<MembersDbContext>();
#if (UsePostgres)
        options.UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused");
        const string provider = "Npgsql.EntityFrameworkCore.PostgreSQL";
#else
        options.UseSqlServer("Server=localhost;Database=unused;Integrated Security=True");
        const string provider = "Microsoft.EntityFrameworkCore.SqlServer";
#endif
        using var context = new MembersDbContext(options.Options);

        context.Database.ProviderName.Should().Be(provider);
        var script = context.Database.GenerateCreateScript();
        script.Should().Contain("CREATE TABLE").And.Contain("Members").And.Contain("TrellisOutboxMessages");
    }
}
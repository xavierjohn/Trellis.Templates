namespace AntiCorruptionLayer.Tests;

using Microsoft.EntityFrameworkCore;
using TodoSample.AntiCorruptionLayer;
using TodoSample.Domain;

public sealed class SelectedProviderSchemaTests
{
    [Fact]
    public void Selected_provider_can_generate_the_sample_schema()
    {
        using var context = CreateContext();
#if (UsePostgres)
        const string provider = "Npgsql.EntityFrameworkCore.PostgreSQL";
#elif (UseSqlServer)
        const string provider = "Microsoft.EntityFrameworkCore.SqlServer";
#else
        const string provider = "Microsoft.EntityFrameworkCore.Sqlite";
#endif

        context.Database.ProviderName.Should().Be(provider);
        var script = context.Database.GenerateCreateScript();
        script.Should().Contain("CREATE TABLE").And.Contain("TodoItems").And.Contain("ETag");
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Selected_provider_preserves_utc_due_dates_when_rehydrating(DateTimeKind storedKind)
    {
        using var context = CreateContext();
        var date = new DateTime(2099, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var dueDate = DueDate.Create(date);
        var property = context.Model.FindEntityType(typeof(TodoItem))!.FindProperty(nameof(TodoItem.DueDate))!;
        var converter = property.GetTypeMapping().Converter!;

        var stored = converter.ConvertToProvider(dueDate).Should().BeOfType<DateTime>().Subject;
        stored.Kind.Should().Be(DateTimeKind.Utc);
        stored.Should().Be(date);
        var rehydrated = converter.ConvertFromProvider(DateTime.SpecifyKind(stored, storedKind))
            .Should().BeOfType<DueDate>().Subject;
        rehydrated.Value.Kind.Should().Be(DateTimeKind.Utc);
        rehydrated.Value.Should().Be(date);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
#if (UsePostgres)
        options.UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused");
#elif (UseSqlServer)
        options.UseSqlServer("Server=localhost;Database=unused;Integrated Security=True");
#else
        options.UseSqlite("Data Source=:memory:");
#endif
        return new AppDbContext(options.Options);
    }
}

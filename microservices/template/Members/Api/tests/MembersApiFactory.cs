using MartinCostello.Logging.XUnit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ProjectTrackerTemplate.Members.Acl;
using Trellis.Asp.Authorization;
using Trellis.Authorization;
using Trellis.EntityFrameworkCore;
using Trellis.Mediator;
using Trellis.Testing.AspNetCore;

namespace Members.Api.Tests;

// Boots the Members host through WebApplicationFactory for HTTP-level integration tests. By default it is
// hermetic — the SQL Server context is swapped for in-memory SQLite, the Service Bus publisher is a no-op,
// and the gateway-JWT auth is swapped for a test scheme + the development actor provider (X-Test-Actor) so
// authorization outcomes run for real without a gateway. Set USE_REAL_SERVICES=true (see .runsettings) to
// run the same tests against the real configured SQL Server + Service Bus instead.
public class MembersApiFactory : WebApplicationFactory<Program>, ITestOutputHelperAccessor
{
    private static bool UseRealServices =>
        string.Equals(Environment.GetEnvironmentVariable("USE_REAL_SERVICES"), "true", StringComparison.OrdinalIgnoreCase);

    private readonly SqliteConnection? _connection;

    public MembersApiFactory()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

        if (!UseRealServices)
        {
            // A single open connection keeps the in-memory SQLite database alive for the fixture's lifetime.
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();
        }
    }

    public ITestOutputHelper? OutputHelper { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureLogging(logging => logging.AddXUnit(this));

        // The test reaches the service host directly (no gateway to mint the internal JWT), so in BOTH
        // modes swap the gateway-JWT auth for a test scheme + the development actor provider (X-Test-Actor).
        builder.ConfigureTestServices(AddTestAuthentication);

        if (UseRealServices)
            return;

        // Hermetic mode: supply the settings Aspire would inject so AddSqlServerDbContext +
        // AddAzureServiceBusClient register, then swap the backends for in-memory SQLite + a no-op publisher.
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:membersdb"] = "Server=(localdb)\\MSSQLLocalDB;Database=members-test",
            ["ConnectionStrings:messaging"] = "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true",
            ["DeployedEnvironment:Region"] = "test",
        }));

        builder.ConfigureTestServices(services =>
        {
            services.ReplaceDbProvider<MembersDbContext>(options =>
                options.UseSqlite(_connection!).AddTrellisInterceptors().AddTrellisOutboxInterceptor());

            // No real broker in hermetic mode — the outbox relay drains into a no-op.
            services.ReplaceSingleton<IIntegrationEventPublisher>(new NoOpIntegrationEventPublisher());
        });
    }

    // Swap the gateway-JWT auth for a scheme that authenticates the request, and resolve the actor from
    // X-Test-Actor (what CreateClientWithActor writes) instead of a real internal JWT. Applies in both modes
    // because a WebApplicationFactory test reaches the service directly, bypassing the gateway that mints it.
    private static void AddTestAuthentication(IServiceCollection services)
    {
        services.AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, null);
        services.RemoveAll<IActorProvider>();
        services.AddDevelopmentActorProvider();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _connection?.Dispose();
    }
}

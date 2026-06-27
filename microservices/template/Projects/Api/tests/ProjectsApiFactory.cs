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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProjectTrackerTemplate.Projects.Acl;
using Trellis.Asp.Authorization;
using Trellis.Authorization;
using Trellis.EntityFrameworkCore;
using Trellis.Testing.AspNetCore;

namespace Projects.Api.Tests;

// Boots the Projects host through WebApplicationFactory for HTTP-level integration tests. By default it is
// hermetic — the SQL Server context is swapped for in-memory SQLite, the Service Bus consumer (which would
// otherwise dial a broker on startup) is removed, and the gateway-JWT auth is swapped for a test scheme +
// the development actor provider (X-Test-Actor) so authorization outcomes run for real without a gateway.
// Set USE_REAL_SERVICES=true (see .runsettings) to run against the real configured SQL Server + Service Bus.
public class ProjectsApiFactory : WebApplicationFactory<Program>, ITestOutputHelperAccessor
{
    private static bool UseRealServices =>
        string.Equals(Environment.GetEnvironmentVariable("USE_REAL_SERVICES"), "true", StringComparison.OrdinalIgnoreCase);

    private readonly SqliteConnection? _connection;

    public ProjectsApiFactory()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

        if (!UseRealServices)
        {
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

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:projectsdb"] = "Server=(localdb)\\MSSQLLocalDB;Database=projects-test",
            ["ConnectionStrings:messaging"] = "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true",
            ["DeployedEnvironment:Region"] = "test",
        }));

        builder.ConfigureTestServices(services =>
        {
            services.ReplaceDbProvider<ProjectsDbContext>(options =>
                options.UseSqlite(_connection!).AddTrellisInterceptors());

            // The Service Bus pump dials the broker on startup; drop it so the host runs without one. The
            // cross-service eventing flow is covered by the in-memory-broker integration test instead. Use a
            // type-safe predicate and fail loudly (Single) if it is no longer registered — e.g. renamed —
            // rather than silently leaving the real pump wired against the dummy connection string.
            var consumer = services.Single(descriptor =>
                descriptor.ServiceType == typeof(IHostedService) &&
                descriptor.ImplementationType == typeof(MemberEventsConsumer));
            services.Remove(consumer);
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

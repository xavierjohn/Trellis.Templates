using System.Security.Claims;
using System.Text.Encodings.Web;
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
using Microsoft.Extensions.Options;
using ProjectTrackerTemplate.Members.Api;
using ProjectTrackerTemplate.Projects.Api;
using Trellis.Asp.Authorization;
using Trellis.Authorization;
using Trellis.EntityFrameworkCore;
using Trellis.Mediator;
using Trellis.Testing.AspNetCore;

namespace Eventing.Tests;

// Authentication scheme for the hermetic eventing test: authenticates the request so RequireAuthorization
// passes; the actor (id + permissions + tenant) comes from X-Test-Actor via the development actor provider.
internal sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}

internal static class EventingTestServices
{
    private const string Messaging =
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true";

    public static IDictionary<string, string?> Configuration(string dbConnectionName) => new Dictionary<string, string?>
    {
        [$"ConnectionStrings:{dbConnectionName}"] = $"Server=(localdb)\\MSSQLLocalDB;Database={dbConnectionName}-test",
        ["ConnectionStrings:messaging"] = Messaging,
        ["DeployedEnvironment:Region"] = "test",
    };

    // Swap the gateway-JWT auth for a test scheme + the development actor provider (X-Test-Actor).
    public static void AddTestAuthentication(IServiceCollection services)
    {
        services.AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, null);
        services.RemoveAll<IActorProvider>();
        services.AddDevelopmentActorProvider();
    }
}

// Boots the Members host wired to the in-memory broker (its outbox relay publishes there) over SQLite.
internal sealed class MembersEventingFactory : WebApplicationFactory<MembersApiEntryPoint>
{
    private readonly InMemoryBroker _broker;
    private readonly SqliteConnection _connection;

    public MembersEventingFactory(InMemoryBroker broker)
    {
        _broker = broker;
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(EventingTestServices.Configuration("membersdb")));

        builder.ConfigureTestServices(services =>
        {
            services.ReplaceDbProvider<ProjectTrackerTemplate.Members.Acl.MembersDbContext>(options =>
                options.UseSqlite(_connection).AddTrellisInterceptors().AddTrellisOutboxInterceptor());
            services.ReplaceSingleton<IIntegrationEventPublisher>(new InMemoryBrokerPublisher(_broker));
            EventingTestServices.AddTestAuthentication(services);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _connection.Dispose();
    }
}

// Boots the Projects host with the in-memory broker consumer (instead of the Service Bus pump) over SQLite.
internal sealed class ProjectsEventingFactory : WebApplicationFactory<ProjectsApiEntryPoint>
{
    private readonly InMemoryBroker _broker;
    private readonly SqliteConnection _connection;

    public ProjectsEventingFactory(InMemoryBroker broker)
    {
        _broker = broker;
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(EventingTestServices.Configuration("projectsdb")));

        builder.ConfigureTestServices(services =>
        {
            services.ReplaceDbProvider<ProjectTrackerTemplate.Projects.Acl.ProjectsDbContext>(options =>
                options.UseSqlite(_connection).AddTrellisInterceptors());

            // Replace the Service Bus pump with the in-memory broker consumer feeding the same inbox. Use a
            // type-safe predicate and fail loudly (Single) if the pump is no longer registered (e.g. renamed).
            var pump = services.Single(descriptor =>
                descriptor.ServiceType == typeof(IHostedService) &&
                descriptor.ImplementationType == typeof(ProjectTrackerTemplate.Projects.Acl.MemberEventsConsumer));
            services.Remove(pump);

            services.AddSingleton<IHostedService>(provider =>
                new InMemoryBrokerConsumer(_broker, provider.GetRequiredService<IInboxDispatcher>()));

            EventingTestServices.AddTestAuthentication(services);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _connection.Dispose();
    }
}

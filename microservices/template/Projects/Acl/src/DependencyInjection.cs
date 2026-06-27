using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProjectTrackerTemplate.Projects.Application;
using Trellis.EntityFrameworkCore;
using Trellis.Mediator;

namespace ProjectTrackerTemplate.Projects.Acl;

// Registers the anti-corruption / infrastructure layer: the in-memory Project store, the EF Core context
// for the inbox + read models (SQL Server via Aspire), the inbound eventing plane (inbox dedup + the
// read-model projection handler + the Service Bus pump), and resource-based authorization. Takes the host
// builder because the Aspire component registrations hang off IHostApplicationBuilder.
public static class DependencyInjection
{
    public static IHostApplicationBuilder AddProjectsAcl(this IHostApplicationBuilder builder)
    {
        // Azure Service Bus client (Aspire injects the "messaging" connection — local emulator in dev).
        builder.AddAzureServiceBusClient(MemberEventsChannel.ConnectionName);

        // EF Core over SQL Server for the inbox dedup table + the read models. Aspire injects "projectsdb";
        // AddTrellisInterceptors wires the value-object column conventions.
        builder.AddSqlServerDbContext<ProjectsDbContext>("projectsdb",
            configureDbContextOptions: options => options.AddTrellisInterceptors());

        var services = builder.Services;

        // The Project aggregate stays in an in-memory store (the auth walkthrough); the read model + inbox
        // use EF Core above. Singleton so the seeded dictionary persists across requests.
        services.AddSingleton<IProjectRepository, InMemoryProjectRepository>();
        services.AddScoped<IKnownMemberDirectory, KnownMemberDirectory>();

        // The inbox: dedup keyed on (ConsumerId, MessageId). ConsumerId must be STABLE across deploys — it
        // is part of the dedup key, so renaming it would reprocess everything still in the redelivery window.
        services.AddTrellisInbox<ProjectsDbContext>(options => options.ConsumerId = "projects");

        // The in-process consumer the inbox dispatcher fans out to, plus the Service Bus pump that receives
        // from the broker and hands each message to the dispatcher.
        services.AddIntegrationEventHandler<MemberInvitedIntegrationEvent, MemberInvitedHandler>();
        services.AddHostedService<MemberEventsConsumer>();

        // Resource-based authorization scans the Application assembly (the commands/queries that bind a
        // Project resource) and this assembly (ProjectResourceLoader), and registers the v4 accessor.
        services.AddResourceAuthorization(
            typeof(GetProjectQuery).Assembly,
            typeof(ProjectResourceLoader).Assembly);

        return builder;
    }

    // Dev-only: create the inbox + read-model schema. Production uses EF migrations. Runs before the
    // Service Bus pump starts (hosted services start on app.Run) so the consumer always finds its tables.
    public static async Task EnsureProjectsCreatedAsync(
        this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProjectsDbContext>();
        await db.Database.EnsureCreatedAsync(cancellationToken);
    }
}

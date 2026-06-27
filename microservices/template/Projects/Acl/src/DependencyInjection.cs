using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProjectTrackerTemplate.Projects.Application;
using Trellis.EntityFrameworkCore;
using Trellis.Mediator;

namespace ProjectTrackerTemplate.Projects.Acl;

// Registers the anti-corruption / infrastructure layer: the EF Core context (SQL Server via Aspire) that
// holds the Project aggregate, the inbox dedup table, and the team read model; the EF repository + unit of
// work; the inbound eventing plane (inbox dedup + the read-model projection handler + the Service Bus
// pump); and resource-based authorization. Takes the host builder because the Aspire component
// registrations hang off IHostApplicationBuilder.
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

        // The Project aggregate, the team read model, and the inbox all use the EF Core context above.
        services.AddScoped<IProjectRepository, EfProjectRepository>();
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

        // The EF unit of work: TransactionalCommandBehavior commits the Project aggregate's changes on
        // command-handler success (e.g. UpdateProject). The inbox manages its OWN transaction in a separate
        // scope, so the two never share a SaveChanges.
        services.AddTrellisUnitOfWork<ProjectsDbContext>();

        return builder;
    }

    // Dev-only: create the inbox + read-model + Project schema. Production uses EF migrations. Runs
    // before the Service Bus pump starts (hosted services start on app.Run) so the consumer always
    // finds its tables.
    public static async Task EnsureProjectsCreatedAsync(
        this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProjectsDbContext>();
        await db.Database.EnsureCreatedAsync(cancellationToken);
        await ProjectsSeed.EnsureSeededAsync(db, cancellationToken);
    }
}

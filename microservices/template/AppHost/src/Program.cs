#pragma warning disable IDE0047
// Aspire AppHost — orchestrates the Project Tracker template topology.
//
//   AppHost
//     ├── projects (audience="projects", /api/projects/* endpoints)
//     ├── members  (audience="members",  /api/members/*  endpoints)
//     └── gateway  (YARP, fixed port 5001, references projects + members)
//
// `dotnet run --project AppHost/src` boots all three processes, wires service-discovery
// env vars so YARP destinations resolve `https+http://projects` to the dynamically-
// assigned Projects port, and opens the Aspire dashboard with logs, traces, and
// metrics flowing in from every service.

using Aspire.Hosting.Azure;
using ProjectTrackerTemplate.SharedKernel;
using Trellis.Mediator;

var builder = DistributedApplication.CreateBuilder(args);

// The two backend microservices. Aspire assigns dynamic ports — Gateway only
// needs the logical name; service discovery handles the rest.
//
// NOTE: the generated type names `Projects.Projects_Api` / `Projects.Members_Api` /
// `Projects.Gateway` are derived from each project's CSPROJ BASE NAME by Aspire's source
// generator (dots in the name become underscores). The outer `Projects` is Aspire's
// namespace; the inner name is our host project (e.g. the Projects service host is `Projects_Api`).

// SQL Server backs both services' data planes. Aspire provisions the container, the per-service
// databases ("membersdb", "projectsdb"), and injects each connection string into its owner.
#if (UsePostgres)
var databaseServer = builder.AddPostgres("postgres");
#else
var databaseServer = builder.AddSqlServer("sql");
#endif
var membersDb = databaseServer.AddDatabase("membersdb");
var projectsDb = databaseServer.AddDatabase("projectsdb");

// Azure Service Bus carries integration events between the services. RunAsEmulator runs the Service
// Bus emulator as a local container (needs Docker) — no Azure subscription for development. Members
// publishes to the contract-named topic; Projects owns its subscription. The explicit Subject rule
// is also required by the emulator, which does not create Azure's implicit match-all rule.
var contracts = IntegrationEventNameMap.FromAssemblies(typeof(MemberInvitedIntegrationEvent).Assembly);
var memberInvitedTopic = contracts.NameFor(typeof(MemberInvitedIntegrationEvent))
    .GetValueOrThrow("MemberInvited must declare an IntegrationEventName.");
var serviceBus = builder.AddAzureServiceBus(MessagingTopology.ConnectionName)
    .RunAsEmulator();
serviceBus.AddServiceBusTopic("member-invited", memberInvitedTopic)
    .AddServiceBusSubscription("projects-member-events", MessagingTopology.ProjectsSubscriptionName)
    .WithProperties(subscription => subscription.Rules.Add(new AzureServiceBusRule("member-invited-subject")
    {
        FilterType = AzureServiceBusFilterType.CorrelationFilter,
        CorrelationFilter = new AzureServiceBusCorrelationFilter { Subject = memberInvitedTopic },
    }));

var projects = builder.AddProject<Projects.Projects_Api>("projects")
    .WithReference(projectsDb)
    .WithReference(serviceBus)
    .WaitFor(projectsDb)
    .WaitFor(serviceBus);

var members = builder.AddProject<Projects.Members_Api>("members")
    .WithReference(membersDb)
    .WithReference(serviceBus)
    .WaitFor(membersDb)
    .WaitFor(serviceBus);

// The gateway is pinned to a stable port so its issuer URL stays constant across
// runs (the JWT 'iss' claim and the consumer-side JwtBearerOptions.Authority both
// have to agree; using service discovery for an OIDC discovery doc fetch is more
// friction than value for a learning template).
var gateway = builder.AddProject<Projects.Gateway>("gateway")
    .WithReference(projects)
    .WithReference(members)
    .WaitFor(projects)
    .WaitFor(members)
    .WithHttpEndpoint(port: 5001, name: "http")
    .WithExternalHttpEndpoints();

var telemetryConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(telemetryConnectionString))
{
    projects.WithEnvironment("APPLICATIONINSIGHTS_CONNECTION_STRING", telemetryConnectionString);
    members.WithEnvironment("APPLICATIONINSIGHTS_CONNECTION_STRING", telemetryConnectionString);
    gateway.WithEnvironment("APPLICATIONINSIGHTS_CONNECTION_STRING", telemetryConnectionString);
}

var cosmosEndpoint = builder.Configuration["Idempotency:Cosmos:Endpoint"];
if (!string.IsNullOrWhiteSpace(cosmosEndpoint))
{
    members.WithEnvironment("Idempotency__Store", "Cosmos")
        .WithEnvironment("Idempotency__Cosmos__Endpoint", cosmosEndpoint);

    var clientId = builder.Configuration["AZURE_CLIENT_ID"];
    if (!string.IsNullOrWhiteSpace(clientId))
        members.WithEnvironment("AZURE_CLIENT_ID", clientId);

    foreach (var setting in new[] { "DatabaseId", "ContainerId" })
    {
        var value = builder.Configuration[$"Idempotency:Cosmos:{setting}"];
        if (!string.IsNullOrWhiteSpace(value))
            members.WithEnvironment($"Idempotency__Cosmos__{setting}", value);
    }
}

builder.Build().Run();
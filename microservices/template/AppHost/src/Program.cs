// Aspire AppHost — orchestrates the Project Tracker template topology.
//
//   AppHost
//     ├── projects (audience="projects", /api/projects/* endpoints)
//     ├── members  (audience="members",  /api/members/*  endpoints)
//     └── gateway  (YARP, fixed port 5001, references projects + members)
//
// `dotnet run --project AppHost` boots all three processes, wires service-discovery
// env vars so YARP destinations resolve `https+http://projects` to the dynamically-
// assigned Projects port, and opens the Aspire dashboard with logs, traces, and
// metrics flowing in from every service.

using ProjectTrackerTemplate.SharedKernel;

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
var sql = builder.AddSqlServer("sql");
var membersDb = sql.AddDatabase("membersdb");
var projectsDb = sql.AddDatabase("projectsdb");

// Azure Service Bus carries integration events between the services. RunAsEmulator runs the Service
// Bus emulator as a local container (needs Docker) — no Azure subscription for development. Members
// publishes MemberInvited to the "member-events" queue and Projects consumes it. The queue name matches
// SharedKernel.MemberEventsChannel so both services bind to it.
var serviceBus = builder.AddAzureServiceBus(MemberEventsChannel.ConnectionName)
    .RunAsEmulator();
serviceBus.AddServiceBusQueue(MemberEventsChannel.QueueName);

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
builder.AddProject<Projects.Gateway>("gateway")
    .WithReference(projects)
    .WithReference(members)
    .WaitFor(projects)
    .WaitFor(members)
    .WithHttpEndpoint(port: 5001, name: "http")
    .WithExternalHttpEndpoints();

builder.Build().Run();

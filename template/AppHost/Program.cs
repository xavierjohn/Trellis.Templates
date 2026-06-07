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

var builder = DistributedApplication.CreateBuilder(args);

// The two backend microservices. Aspire assigns dynamic ports — Gateway only
// needs the logical name; service discovery handles the rest.
//
// NOTE: the generated type names `Projects.Projects` / `Projects.Members` /
// `Projects.Gateway` are derived from each project's CSPROJ BASE NAME by Aspire's
// source generator. The repeated `Projects.Projects` looks odd but is correct — the
// outer `Projects` is Aspire's namespace; the inner `Projects` is our project name.
var projects = builder.AddProject<Projects.Projects>("projects");
var members = builder.AddProject<Projects.Members>("members");

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

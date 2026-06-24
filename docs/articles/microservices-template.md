# Microservices template

> `dotnet new trellis-microservices` · NuGet package **`Trellis.Microservices.Templates`**

A multi-tenant **microservices topology** orchestrated by [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/),
built on [Trellis](https://github.com/xavierjohn) and Trellis.Microservices. Reach for this when you're
building a platform of services that need a shared gateway, tenant isolation, and service-to-service
authorization.

## Topology

```
MyPlatform/
├── AppHost/          # .NET Aspire orchestrator — runs everything together, wires service discovery
├── ServiceDefaults/  # Shared library: OpenTelemetry, health checks, resilience, service discovery
├── Gateway/          # YARP reverse proxy — the public edge; mints internal-network JWTs
├── Members/          # An opinionated downstream service
└── Projects/         # A second downstream service
```

- **AppHost** references the three service projects and starts them as one application. `dotnet run --project AppHost`
  brings the whole platform up with the Aspire dashboard.
- **ServiceDefaults** is referenced by every service and centralizes telemetry, health, and resilience so
  the services stay thin and consistent.
- **Gateway** is the only public entry point. It authenticates the caller and mints a short-lived
  **internal JWT** that downstream services trust — so the services never face the public internet directly.

## What it demonstrates

- **Multi-tenancy** — tenant isolation carried through the gateway into each service.
- **RBAC + ABAC** — role- and attribute-based access, plus resource-based authorization, on the message.
- **The HideExistence pattern** — returning `404` instead of `403` for HR-sensitive resources, so you can't
  probe for what exists.
- **End-to-end observability** — a request is traced from the gateway, across services, through the mediator
  pipeline, with business events sharing the same trace.

## Versioned minimal-API endpoints

Each service exposes **date-versioned minimal-API endpoint groups**. Authorization, the API version, and
Service Level Indicators are applied once at the group level, so individual endpoints stay declarative:

```csharp
var group = app.MapGroup("/api/members")
    .MapToApiVersion(...)
    .RequireAuthorization()
    .AddServiceLevelIndicator();

group.MapGet("/{id}", ...);
group.MapPost("/", ...);
```

Commands and their handlers are colocated in one file, named after the message, so a feature is easy to find
and read top-to-bottom.

## Running it

```bash
cd MyPlatform
dotnet run --project AppHost
```

The Aspire dashboard (URL printed on startup) gives you live logs, traces, and metrics across every service.
Each service also serves its own OpenAPI document and Scalar UI, and the click-to-send `.http` files under
each service drive end-to-end scenarios.

## Build

```bash
dotnet build MyPlatform.slnx -c Release    # 0 warnings, 0 errors; Release enforces analyzers + code-style
```

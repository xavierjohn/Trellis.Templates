# ASP.NET service template

> `dotnet new trellis-asp` · NuGet package **`Trellis.AspTemplate`**

A single, well-structured ASP.NET Core service built on the [Trellis](https://github.com/xavierjohn)
framework, using **Domain-Driven Design** and **Railway-Oriented Programming**. Reach for this when you're
building one focused service rather than a fleet of them.

## Solution structure

The template follows a strict, inward-pointing dependency flow:

```
MyService/
├── Domain/        # Aggregates, entities, value objects, domain events, specifications
├── Application/   # Commands, queries, handlers, repository interfaces
├── Acl/           # DbContext, EF Core configuration, repository implementations (the Anti-Corruption Layer)
├── Api/           # Controllers, DTOs, Program.cs, the composition root
└── *.slnx         # Solution file
```

| Layer | May depend on | Never depends on |
| --- | --- | --- |
| **Domain** | Trellis primitives only | EF Core, ASP.NET, Mediator |
| **Application** | Domain, Mediator | ASP.NET, EF providers |
| **Acl** | Application, EF Core | Api |
| **Api** | Application, Acl | Domain persistence details |

> **Why "Acl"?** It's the *Anti-Corruption Layer* — it adapts external systems (SQL Server, queues, other
> services) to your domain model, instead of the vaguer "Infrastructure".

## What's wired up

Out of the box the service has every [cross-cutting capability](capabilities.md): date-based API
versioning, Service Level Indicators, idempotent writes, RFC 9457 ProblemDetails, an OpenAPI document with
a Scalar UI, OpenTelemetry, actor-based authorization, the mediator pipeline, and health checks. EF Core is
configured with Trellis conventions and interceptors.

## API versioning by namespace

Controllers live in date-stamped folders and namespaces — one folder per version:

```
Api/src/2026-03-26/Controllers/TodosController.cs   →  namespace MyService.Api.v2026_03_26.Controllers
Api/src/2026-12-01/Controllers/TodosController.cs   →  namespace MyService.Api.v2026_12_01.Controllers
```

The version is derived from the namespace — no `[ApiVersion]` attributes to maintain. To add a version, copy
the latest folder, rename the namespace, and evolve it independently. Older versions stay frozen.

## The reference implementation

A small **Todo** domain ships as a worked example of every pattern: scalar and composite value objects, a
`RequiredEnum` smart enum, an aggregate with a lazy state machine, always-valid commands with `TryCreate`,
resource-based authorization, repositories returning `Maybe<T>`, ETag concurrency, and version-mapped
controllers. Read it before replacing it — it's the fastest way to learn the conventions.

## Build and test

```bash
dotnet build MyService.slnx -c Release        # Release enforces code-style as errors
dotnet test  --solution MyService.slnx -c Release --filter-not-trait "Category=Integration"
```

Tests use **xUnit v3 on Microsoft.Testing.Platform** — don't pass legacy VSTest flags such as `--nologo`
or `--logger`; the runner rejects them.

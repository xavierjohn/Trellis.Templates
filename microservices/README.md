# Trellis.Microservices.Templates

[![Build Template](https://github.com/xavierjohn/Trellis.Templates/actions/workflows/build-templates.yml/badge.svg)](https://github.com/xavierjohn/Trellis.Templates/actions/workflows/build-templates.yml)
[![NuGet](https://img.shields.io/nuget/v/Trellis.Microservices.Templates.svg)](https://www.nuget.org/packages/Trellis.Microservices.Templates)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Trellis.Microservices.Templates.svg)](https://www.nuget.org/packages/Trellis.Microservices.Templates)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/download)
[![C#](https://img.shields.io/badge/C%23-14.0-blue.svg)](https://docs.microsoft.com/en-us/dotnet/csharp/)
[![GitHub Stars](https://img.shields.io/github/stars/xavierjohn/Trellis.Microservices.Template?style=social)](https://github.com/xavierjohn/Trellis.Microservices.Template/stargazers)

> `dotnet new trellis-microservices` template scaffolding a multi-tenant microservices topology on the [Trellis framework](https://github.com/xavierjohn/Trellis) and the [Trellis.Microservices](https://github.com/xavierjohn/Trellis.Microservices) packages.

## Quick start

```bash
dotnet new install Trellis.Microservices.Templates
dotnet new trellis-microservices -n MyOrg.Tracker
cd MyOrg.Tracker
dotnet run --project AppHost/src
```

You get an Aspire-orchestrated **Project Tracker** topology. Open `AppHost/src/*.http` for the
authorization and cross-service eventing scenarios. Docker or Podman must be running.

## What gets scaffolded

| Project | Role | Auth shape |
|---|---|---|
| **`Gateway`** | YARP reverse proxy. Authenticates the public JWT, then re-mints a per-cluster **internal-network JWT** carrying the full `Actor` (id + permissions + forbidden permissions + ABAC attributes). Exposes `/.well-known/openid-configuration` + `/.well-known/jwks.json` so downstream services configure `AddJwtBearer(o.Authority = gatewayUrl)` for transparent key rotation. | Mints |
| **`Projects`** | Operational cluster (CRUD on a `Project` aggregate). Cross-tenant access returns **403**. Demonstrates the **v4 typed accessor** (`IAuthorizedResource<TMessage, Project>`) so handlers load the project **once** and the pipeline reuses it for both authorization and the handler body. | Standard |
| **`Members`** | HR-sensitive cluster (CRUD on a `Member` aggregate). Cross-tenant access returns **404** via `HideExistence<Member>()` — prevents enumeration of employees across tenant boundaries. **Asymmetric authorization across two clusters in one solution** is the teaching moment. | Hide-existence |
| **`AppHost`** | Aspire orchestrator wiring all three services + OpenTelemetry dashboard + dev-only JWT signing-key minting on startup. | — |
| **`ServiceDefaults`** | Shared OpenTelemetry + health-check + service-discovery extensions. | — |

## What it demonstrates

- **Tenant isolation (ABAC)** — `tenant_id` claim required on every internal JWT; missing it produces a startup-validated 401, not a silent privilege escalation.
- **Resource-based authorization** — `IAuthorizedResource<TMessage, Project>` loads the aggregate once at the pipeline boundary; handler bodies receive the already-authorized resource. No double-fetch.
- **HideExistence pattern** — `o.HideExistence<Member>()` collapses cross-tenant 403 → 404 to prevent enumeration of HR-sensitive resources.
- **JWT contract integrity** — sentinel + count claims defend the deny-overrides-allow invariant against a proxy stripping the deny set.
- **Transparent key rotation** — gateway publishes JWKS at a stable URL; downstream services rotate keys with zero config changes via `AddJwtBearer(o.Authority = gatewayUrl)`.
- **Multi-tenant from line 1** — `tenant_id` is in the contract, the actor provider, every resource loader, and every test fixture. There is no "add multi-tenancy later" path.

## Template parameters

| Parameter | Default | Description |
|---|---|---|
| `-n`, `--name` | `MyTracker` | Solution name. Becomes the root namespace and assembly-name prefix. Hyphens and other non-identifier characters are sanitized to underscores. |
| `--author-name` | `Your Name` | Author written into `Directory.Build.props`. |
| `--root-namespace` | Derived from `-n` | Valid C# root namespace and assembly prefix. |
| `--api-versioning` | `false` | Date-versioned HTTP APIs; event and internal-JWT versions are independent. |
| `--database` | `sqlserver` | `sqlserver` or `postgres`; Aspire provisions the selected provider. |
| `--auth` | `jwt` | Gateway external `jwt`/OIDC or `entra`; downstream internal JWTs are unchanged. |
| `--telemetry-exporters` | `otlp` | `otlp`, `azure-monitor`, or `both`. |
| `--deployment` | `none` | `none`, `container`, or `azure` (Container Apps). |
| `--gateway-issuer-url` | `http://localhost:5001` | Development gateway issuer; production requires an explicit HTTPS `Gateway:Issuer`. |
| `--skip-restore` | `false` | Skip post-creation `dotnet restore`. |

Azure requires an explicit `--database`. Production requires external identity, persistent RSA signing
material, separate service database credentials, Service Bus, and Cosmos idempotency.
See the [shared defaults](../README.md#generation-options) and the generated deployment README.

> **Want a smaller starter?** To drop the Members service after instantiation:
> 1. Delete the `Members/` directory.
> 2. Remove the `<Project Path="Members/Members.csproj" />` line from the generated `.slnx` (also drop the matching `<File Path>` entry inside the Solution Items folder).
> 3. In `AppHost/Program.cs`: remove the `var members = ...` line and any `WithReference(members)` / `WaitFor(members)` references; in `Gateway/appsettings.json` drop the `members-route` route and `members` cluster.

## Versioning

This template tracks the [Trellis.Microservices](https://github.com/xavierjohn/Trellis.Microservices) packages 1:1 — `alpha` template pulls `alpha` runtime. Both repos use [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning) so the height-based version is identical when built from the same commit.

## Related repositories

- [`xavierjohn/Trellis`](https://github.com/xavierjohn/Trellis) — the framework: `Result<T>`, `Maybe<T>`, value objects, DDD primitives, ASP.NET / EF Core / Mediator integration.
- [`xavierjohn/Trellis.Microservices`](https://github.com/xavierjohn/Trellis.Microservices) — the building blocks this template instantiates: YARP gateway integration + consumer-side actor provider.
- [ASP.NET template](../asp/README.md) — single-service Clean Architecture template.

## License

[MIT](LICENSE).
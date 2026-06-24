# Trellis Templates

**Opinionated .NET service templates that are correct by construction.** Pick one with `dotnet new` and
you start from a service that already has versioned APIs, observability, authorization, idempotency, and
RFC-compliant error handling wired up — not a blank `Program.cs` you have to harden yourself.

There are two templates, and a contract that keeps them honest.

| | [ASP.NET service](articles/asp-template.md) | [Microservices](articles/microservices-template.md) |
| --- | --- | --- |
| **For** | One focused service | A platform of services behind a gateway |
| **`dotnet new`** | `trellis-asp` | `trellis-microservices` |
| **NuGet** | `Trellis.AspTemplate` | `Trellis.Microservices.Templates` |
| **Shape** | Domain / Application / Acl / Api | Aspire AppHost + ServiceDefaults + Gateway + services |

Both ship the **same cross-cutting capabilities**, enforced by an executable
[capability-parity contract](articles/capability-parity.md) so neither template can silently fall behind
the other.

## Quick start

```bash
# Install the templates (one-time)
dotnet new install Trellis.AspTemplate
dotnet new install Trellis.Microservices.Templates

# Scaffold a project
dotnet new trellis-asp -n MyService
dotnet new trellis-microservices -n MyPlatform
```

➡️ **[Full getting-started walkthrough](articles/getting-started.md)**

## What you get out of the box

- **[Date-based API versioning](articles/capabilities.md#api-versioning)** — version your API by date, no breaking changes by accident.
- **[Service Level Indicators](articles/capabilities.md#service-level-indicators)** — per-operation latency/availability metrics, named from the route.
- **[Idempotent writes](articles/capabilities.md#idempotency)** — safe retries via the `Idempotency-Key` header.
- **[RFC 9457 ProblemDetails](articles/capabilities.md#problem-details)** — every error is a standard, machine-readable problem document.
- **[OpenAPI + Scalar](articles/capabilities.md#openapi--scalar)** — a per-version OpenAPI document and a modern API reference UI.
- **[OpenTelemetry observability](articles/capabilities.md#observability)** — traces, metrics, and logs, with mediator spans and business events correlated end-to-end.
- **[Actor-based authorization](articles/capabilities.md#authorization)** — permission and resource checks declared on the message, enforced by the pipeline.
- **[Value-object validation](articles/capabilities.md#scalar-value-validation)** — a malformed id in the URL returns `422`, not a `500`.

## Why one repository

A developer who picks the "wrong" template should not silently lose a guardrail. Keeping both templates in
one repository lets a single, **executable** contract assert that every cross-cutting capability exists in
both — drift is caught by CI, not by hoping someone remembers. See
[Capability parity](articles/capability-parity.md).

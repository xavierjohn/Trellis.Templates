# Trellis project templates

[![Build templates](https://github.com/xavierjohn/Trellis.Templates/actions/workflows/build-templates.yml/badge.svg)](https://github.com/xavierjohn/Trellis.Templates/actions/workflows/build-templates.yml)
[![Capability parity](https://github.com/xavierjohn/Trellis.Templates/actions/workflows/contract-parity.yml/badge.svg)](https://github.com/xavierjohn/Trellis.Templates/actions/workflows/contract-parity.yml)
[![Docs](https://github.com/xavierjohn/Trellis.Templates/actions/workflows/docs.yml/badge.svg)](https://xavierjohn.github.io/Trellis.Templates/)
[![Trellis.AspTemplate](https://img.shields.io/nuget/vpre/Trellis.AspTemplate?label=Trellis.AspTemplate)](https://www.nuget.org/packages/Trellis.AspTemplate)
[![Trellis.Microservices.Templates](https://img.shields.io/nuget/vpre/Trellis.Microservices.Templates?label=Trellis.Microservices.Templates)](https://www.nuget.org/packages/Trellis.Microservices.Templates)

Production-ready `dotnet new` templates for building **.NET 10** services on the
[Trellis](https://github.com/xavierjohn/Trellis) framework — Domain-Driven Design + Railway-Oriented
Programming, with API versioning, authorization, observability, tests, and AI coding guidance wired in
from the first commit. Scaffold a service, press run, and start building.

## Choose a template

| I want to build... | Template | `dotnet new` id | NuGet |
| --- | --- | --- | --- |
| A single ASP.NET Core service | **ASP.NET service** | `trellis-asp` | [`Trellis.AspTemplate`](https://www.nuget.org/packages/Trellis.AspTemplate) |
| A multi-service platform (gateway + services, .NET Aspire) | **Microservices** | `trellis-microservices` | [`Trellis.Microservices.Templates`](https://www.nuget.org/packages/Trellis.Microservices.Templates) |

Start with **`trellis-asp`** when you are building one service. Reach for **`trellis-microservices`**
when you need multiple services behind a gateway with asynchronous, cross-service messaging.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- **Microservices template only:** Docker or Podman running (Aspire runs SQL Server and the Service Bus
  emulator as containers)

## Get started

### Single service — `trellis-asp`

```bash
dotnet new install Trellis.AspTemplate
dotnet new trellis-asp -n MyService
cd MyService
dotnet run --project Api/src
```

Open the **Scalar API reference** at the URL printed on startup, or fire the ready-made requests in
`Api/src/api.http`. No containers or external database are needed to run the sample, and the generated
solution builds and its tests pass out of the box (`dotnet test`). You get a working **Todo** service —
delete it and build your own.

### Microservices platform — `trellis-microservices`

```bash
dotnet new install Trellis.Microservices.Templates
dotnet new trellis-microservices -n MyPlatform
cd MyPlatform
dotnet run --project AppHost/src
```

This boots the **.NET Aspire dashboard** (<http://localhost:15151>) and brings up a **Gateway**
(YARP reverse proxy + internal-JWT minting) in front of two reference services — **Members** and
**Projects**. Open `AppHost/src/*.http` for click-to-run scenarios that walk the authorization outcomes
and the cross-service eventing flow (invite a member, then watch them appear in another service's team
list). The generated `README.md` is a full guided tour.

## What every generated project gives you

Both templates scaffold a clean, layered service (**Domain -> Application -> Acl -> Api**, each with its
own `src/` and `tests/`) built around Trellis's `Result<T>` / `Maybe<T>` and always-valid value objects —
plus these cross-cutting capabilities, already wired and tested:

- **Date-based API versioning**
- **Actor-based authorization** — permission gates and per-resource/ownership checks
- **RFC 9457 ProblemDetails** error responses
- **RFC 9110 conditional requests** — strong ETags, `If-Match` (412 / 428), `If-None-Match` -> 304
- **RFC 8288 cursor (keyset) pagination** with `Link` headers
- **RFC 7240 `Prefer: return=minimal`** on writes
- **Idempotent writes** (`Idempotency-Key`)
- **Value-object validation** — malformed scalars are rejected as 422 at the boundary, and commands are always-valid via `TryCreate`
- **OpenAPI document + Scalar UI**
- **OpenTelemetry** traces, metrics, and structured logs (including Service Level Indicators)
- **Health checks**
- **EF Core** with Trellis conventions (value-object mapping, timestamps, ETag/concurrency interceptors)
- **Mediator pipeline** — authorization and transactional unit-of-work behaviors

The microservices template adds the distributed pieces: a **YARP gateway** with internal-JWT minting and
a JWKS endpoint, **multi-tenant ABAC** isolation, and a **transactional outbox -> Azure Service Bus ->
inbox** flow for effectively-once, cross-service messaging.

## Built for AI-assisted development

Every generated project ships a `.github/copilot-instructions.md` and a full set of `trellis-api-*.md`
API references, so GitHub Copilot and coding agents produce idiomatic Trellis code — `Result`/`Maybe`
flows, always-valid commands, EF conventions — instead of guessing.

## Docs and related projects

- **Trellis framework docs:** <https://xavierjohn.github.io/Trellis/>
- **This repo's docs site:** <https://xavierjohn.github.io/Trellis.Templates/>
- **Framework:** [`xavierjohn/Trellis`](https://github.com/xavierjohn/Trellis) — `Result<T>`, `Maybe<T>`,
  value objects, DDD primitives, ASP.NET / EF Core / Mediator integration
- **Microservices packages:** [`xavierjohn/Trellis.Microservices`](https://github.com/xavierjohn/Trellis.Microservices) —
  YARP gateway integration + consumer-side actor provider for multi-tenant ABAC
- **Resource naming:** [`xavierjohn/Trellis.ResourceNaming`](https://github.com/xavierjohn/Trellis.ResourceNaming) —
  deterministic, CAF-aligned Azure resource names and endpoints; consumed by both templates as a
  published package

---

## Contributing to the templates

The two templates live in one repository so a single **capability-parity contract** keeps them from
drifting apart: a developer who picks either template should get the same guardrails.
[`shared/capability-parity-manifest.yaml`](shared/capability-parity-manifest.yaml) is the source of truth
for the required cross-cutting capabilities. CI runs [`shared/contract-tests/`](shared/contract-tests/)
against each template's source and fails the build if a required capability is missing or regressed —
drift is caught by CI, not by human discipline. (A separate workflow instantiates each template with
`dotnet new` and builds the generated solution end to end.)

```
asp/            trellis-asp             ASP.NET single-service template
microservices/  trellis-microservices   Aspire multi-service template
shared/
  capability-parity-manifest.yaml       required capabilities (source of truth)
  contract-tests/                       runner that asserts each template implements the manifest
  conventions/                          cross-template conventions (e.g. Azure resource naming)
```

Run the contract check locally (against a template's source — no instantiation needed):

```bash
dotnet run --project shared/contract-tests -- shared/capability-parity-manifest.yaml microservices microservices/template
```

## License

MIT.
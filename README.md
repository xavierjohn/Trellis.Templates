# Trellis.Templates

The official [Trellis](https://github.com/xavierjohn) project templates, consolidated into a single
repository with a **shared capability-parity contract** so the templates never silently drift apart.

## Templates

| Template | `dotnet new` id | NuGet package | Folder |
| --- | --- | --- | --- |
| ASP.NET service (single app) | `trellis-asp` | `Trellis.AspTemplate` | [`asp/`](asp/) |
| Microservices (Aspire, multi-service) | `trellis-microservices` | `Trellis.Microservices.Templates` | [`microservices/`](microservices/) |

```bash
dotnet new install Trellis.AspTemplate
dotnet new install Trellis.Microservices.Templates

dotnet new trellis-asp -n MyService
dotnet new trellis-microservices -n MyPlatform
```

## Why one repo

Both templates must offer the same cross-cutting capabilities — API versioning, SLI, idempotency,
ProblemDetails, OpenAPI/Scalar, observability, authorization, the mediator pipeline, health checks —
or a developer who picks the "wrong" one silently loses a guardrail. Keeping the templates in one repo
lets a single, executable contract enforce that parity.

## Layout

```
asp/            # Trellis.AspTemplate     — ASP.NET single-app template (full git history preserved)
microservices/  # Trellis.Microservices.Templates — Aspire multi-service template (history preserved)
shared/
  capability-parity-manifest.yaml   # the single source of truth for required capabilities
  contract-tests/                   # runner that asserts each template implements the manifest
  conventions/                      # cross-template conventions (e.g. Azure resource naming)
```

## Capability parity (enforced, not aspirational)

[`shared/capability-parity-manifest.yaml`](shared/capability-parity-manifest.yaml) lists every
capability and how to verify it (a regex in the instantiated source, a referenced package, or an
HTTP status from the booted app). CI instantiates each template with `dotnet new`, then runs the
contract test in [`shared/contract-tests/`](shared/contract-tests/). A missing or regressed
capability fails the build — drift is caught by CI, not by human discipline.

Run it locally:

```bash
dotnet new trellis-microservices -n _check
dotnet run --project shared/contract-tests -- shared/capability-parity-manifest.yaml microservices ./_check
```

A capability marked `status: planned` (e.g. `resource-naming`) is reported but does not fail the build
until it ships.

## Conventions

Cross-template conventions live under [`shared/conventions/`](shared/conventions/). See
[`azure resource naming`](shared/conventions/resource-naming.md) for the hyperscale resource-naming
convention the templates adopt.

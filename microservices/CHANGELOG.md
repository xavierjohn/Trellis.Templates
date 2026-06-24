# Changelog

All notable changes to the Trellis.Microservices.Templates NuGet template pack are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Version numbers are produced by Nerdbank.GitVersioning from `version.json` plus the git commit height.

## Unreleased

### Added
- Bootstrap of `xavierjohn/Trellis.Microservices.Template`.
- `dotnet new trellis-microservices` template scaffolding a Project Tracker topology:
  - `Gateway` — YARP reverse proxy minting per-cluster internal JWTs, JWKS + OIDC discovery endpoints, dev-only RSA signing-key generation at startup.
  - `Projects` — operational cluster with v4 typed accessor (`IAuthorizedResource<TMessage, Project>`); cross-tenant access returns 403.
  - `Members` — HR-sensitive cluster with `HideExistence<Member>()`; cross-tenant access returns 404 (optional, gated by `--includeMembersService`).
  - `AppHost` — Aspire orchestration of all three services + dev-only JWT signing-key minting.
  - `ServiceDefaults` — shared OpenTelemetry, health checks, and service discovery extensions.
- Template parameters: `-n` (solution name), `--authorName`, `--gatewayIssuerUrl`, `--includeMembersService`, `--skipRestore`.
- CI workflows: `build.yml` (build content + pack + instantiate + build instantiation) and `publish.yml` (gated dry-run + nuget.org publish).
- Aligned analyzer + code-style gates with the upstream `xavierjohn/Trellis.Microservices` repo: `TreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true`, `IDE0005=warning`, `GenerateDocumentationFile=true`.
- Trellis.Microservices runtime pins (`Trellis.Microservices.Abstractions`, `Trellis.Microservices.AspNetCore`, `Trellis.Yarp`) tracked at `0.1.0-alpha.29`.

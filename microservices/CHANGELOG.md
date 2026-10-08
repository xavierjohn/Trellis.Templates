# Changelog

All notable changes to the Trellis.Microservices.Templates NuGet template pack are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Version numbers are produced by Nerdbank.GitVersioning from `version.json` plus the git commit height.

## Unreleased

### Changed
- Upgrade Trellis to `3.0.0-alpha.557` and Trellis.Microservices to `0.1.0-alpha.76`, with version-aligned AgentDocs guidance.
- Use the common `Trellis.Asp` pagination builder in both profiles and explicitly enable its version-aware policy only in versioned hosts.
- Separate the `ProjectTracker.slnx` filename from the namespace placeholder so custom namespaces preserve AgentDocs entrypoints and HTTP solution items.
- Upgrade Aspire packages and the AppHost SDK to `13.6.1`, CodeCoverage to `18.12.0`, and TrxReport to `2.5.1`.
- Replace removed nullable `ToResult` calls with concise, lazy `Result.EnsureNotNull(value, fieldName, detail)` guards and standard `value.not-null` codes, retaining field pointers, details, and accumulated validation errors.

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

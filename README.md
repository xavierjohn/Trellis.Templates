# Trellis.Microservices.Template

> A `dotnet new` template that scaffolds a multi-tenant microservices topology built on the [Trellis framework](https://github.com/xavierjohn/Trellis) and the [Trellis.Microservices](https://github.com/xavierjohn/Trellis.Microservices) packages.

```bash
dotnet new install Trellis.Microservices.Templates
dotnet new trellis-microservices -n MyOrg.Tracker
cd MyOrg.Tracker
dotnet run --project AppHost
```

You get a working **Project Tracker** topology — Aspire-orchestrated — that demonstrates everything Trellis was built for in roughly 1500 lines of code:

| Project | Role | Auth shape |
|---|---|---|
| **`Gateway`** | YARP reverse proxy. Authenticates the public JWT, then re-mints a per-cluster **internal-network JWT** carrying the full `Actor` (id + permissions + forbidden permissions + ABAC attributes). Exposes `/.well-known/openid-configuration` + `/.well-known/jwks.json` so downstream services configure `AddJwtBearer(o.Authority = gatewayUrl)` for transparent key rotation. | Mints |
| **`Projects`** | Operational cluster (CRUD on a `Project` aggregate). Cross-tenant access returns **403** — operational resources tell the caller they aren't allowed. Demonstrates the **v4 typed accessor** (`IAuthorizedResource<TMessage, Project>`) so handlers load the project **once** and the pipeline reuses it for both authorization and the handler body. | Standard |
| **`Members`** | HR-sensitive cluster (CRUD on a `Member` aggregate). Cross-tenant access returns **404** via `HideExistence<Member>()` — prevents enumeration of employees across tenant boundaries. **Asymmetric authorization across two clusters in one solution** is the teaching moment. | Hide-existence |
| **`AppHost`** | Aspire orchestrator wiring all three services + OpenTelemetry dashboard + dev-only JWT signing-key minting on startup. | — |
| **`ServiceDefaults`** | Shared OpenTelemetry + health-check + service-discovery extensions. | — |

## What it demonstrates

- **Tenant isolation (ABAC)** — `tenant_id` claim required on every internal JWT; missing it produces a startup-validated 401, not a silent privilege escalation.
- **Resource-based authorization** — `IAuthorizedResource<TMessage, Project>` loads the aggregate once at the pipeline boundary; handler bodies receive the already-authorized resource. No double-fetch.
- **HideExistence pattern** — `o.HideExistence<Member>()` collapses cross-tenant 403 → 404 to prevent enumeration of HR-sensitive resources.
- **JWT contract integrity** — the gateway mints sentinel + count claims (`trellis_actor_contract_version=1`, `trellis_permissions_count`, `trellis_forbidden_permissions_count`) that the consumer-side `TrellisInternalJwtActorProvider` enforces — defending the "deny overrides allow" invariant against a proxy stripping the deny set.
- **Transparent key rotation** — gateway publishes JWKS at a stable URL; downstream services rotate keys with zero config changes via `AddJwtBearer(o.Authority = gatewayUrl)`.
- **Multi-tenant from line 1** — `tenant_id` is in the contract, the actor provider, every resource loader, and every test fixture. There is no "add multi-tenancy later" path.

## Template parameters

| Parameter | Default | Description |
|---|---|---|
| `-n`, `--name` | `MyTracker` | Solution name. Becomes the root namespace and assembly-name prefix. Spaces are stripped. |
| `--authorName` | `Your Name` | Author written into `Directory.Build.props`. |
| `--gatewayIssuerUrl` | `https://gateway.internal` | Issuer/audience prefix the gateway mints internal JWTs against. |
| `--skipRestore` | `false` | Skip post-creation `dotnet restore`. |

> **Want a smaller starter?** To drop the Members service after instantiation:
> 1. Delete the `Members/` directory.
> 2. Remove the `<Project Path="Members/Members.csproj" />` line from the generated `.slnx`.
> 3. In `AppHost/Program.cs`: remove the `var members = ...` line, the `WithReference(members)`/`WaitFor(members)` lines, and the `members` route from `Gateway/appsettings.json`.

## Versioning

This template tracks the [Trellis.Microservices](https://github.com/xavierjohn/Trellis.Microservices) packages 1:1 — `alpha` template pulls `alpha` runtime; both repos use [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning) so the height-based version is identical when built from the same commit.

## License

MIT.

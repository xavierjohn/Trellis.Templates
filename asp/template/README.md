# TrellisAspTemplate

This Todo service demonstrates Trellis's layered architecture, typed domain model, authorization,
conditional writes, cursor pagination, and idempotency. Start coding agents at `AGENTS.md`.

## Selected profile

`.trellis-template.json` records generation choices. API versioning: `TEMPLATE_API_VERSIONING`;
database: `TEMPLATE_DATABASE_PROVIDER`; identity: `TEMPLATE_AUTH_PROVIDER`;
exporters: `TEMPLATE_TELEMETRY_EXPORTERS`; deployment: `TEMPLATE_DEPLOYMENT_MODE`.

Unversioned controllers/models have undated folders and namespaces. Versioned output uses independent
date-based controllers and version-aware Location/pagination links.

## Due dates

Create and update requests require ISO 8601 due dates with `Z` or an explicit timezone offset, such as
`2099-01-01T12:00:00Z` or `2099-01-01T12:00:00+05:30`. Dates without a timezone return `422`
with a `dueDate` validation error. Offsets are normalized to UTC before validation and persistence;
responses return the same instant in UTC. Programmatic `DueDate` construction requires a UTC
`DateTime`. This policy is the same for every database provider and API version.

## Development

```powershell
dotnet run --project Api\src
```

SQLite needs no container. Server-provider profiles include `compose.database.yaml`:

```powershell
$env:DATABASE_PASSWORD = Read-Host 'Local database password'
docker compose -f compose.database.yaml up -d
```

Set `ConnectionStrings:DefaultConnection` using user secrets or environment configuration:

| Provider | Local connection string |
| --- | --- |
| PostgreSQL | `Host=localhost;Port=5432;Database=todos;Username=trellis;Password=<local-password>` |
| SQL Server | `Server=localhost,1433;Database=todos;User ID=sa;Password=<local-password>;Encrypt=True;TrustServerCertificate=True` |

Administrator credentials and the trust override above are **local development only**. Use
`dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<connection-string>" --project Api\src`
instead of committing credentials. Development creates the sample schema; production does not.
Use `Api\src\api.http` for the Development-only actors and the URL printed for Scalar.

## Production and containers

Outside Development, set the selected external identity: JWT/OIDC needs an HTTPS
`Authentication:Authority` and `Authentication:Audience`; Entra needs GUID `Authentication:TenantId`
and `Authentication:ClientId`. JWT subjects use `sub`; Entra subjects use `oid`.
Permission claims must match the sample's `todos:*` permissions. `X-Test-Actor` is not production identity.

Configure durable idempotency with `Idempotency:Store=Cosmos` and the Cosmos endpoint, database ID, and
container ID. Grant the application's Azure credential native Cosmos data-contributor access scoped
to the container (partition `/scope`, per-item TTL enabled). Account keys are not used.

Activate selected exporters with `OTEL_EXPORTER_OTLP_ENDPOINT` / `OTEL_EXPORTER_OTLP_PROTOCOL`
or `APPLICATIONINSIGHTS_CONNECTION_STRING`. An exporter is inactive without its destination.

Container profiles include `Dockerfile` and `compose.yaml`. Supply `DEPLOYMENT_REGION`, the required environment variables,
selected-provider connection string, Cosmos Azure credential, and external identity before
`docker compose up --build`. Put an HTTPS reverse proxy in front of port 8080; this compose file does
not terminate TLS. The SQLite profile uses a named `/data` volume; server profiles do not.

Apply provider-specific EF migrations with a schema-deployment credential before serving traffic.
The local `EnsureCreated` sample is not a production migration mechanism.
Azure profiles include `deploy\README.md` for App Service provisioning and publishing.

## Guidance maintenance

From this generated project's Git root, run `dotnet tool restore`, `dotnet restore`,
`dotnet tool run agentdocs sync`, and `dotnet tool run agentdocs check --strict`.
Commit `.agentdocs`, its policy, the tool manifest, and managed instruction pointers with package changes.
The application builds and runs without AgentDocs.

# Azure App Service deployment

This scaffold is generated only for `--deployment azure`. Select `--database postgres` or
`--database sqlserver` explicitly. SQLite/Azure is rejected during restore/build and deployment
preflight; no provider is silently substituted.

`deploy\names` uses `Trellis.ResourceNaming.Azure` to compute resource names. The global stack owns
the selected database server/database and keyless Cosmos idempotency store. Each regional stack owns
an identity, workspace, App Service plan/app, and Application Insights when selected. The script uses
the **actual provisioned database FQDN**, not a guessed DNS suffix.

## Prerequisites and configuration

Use PowerShell 7, .NET 10, Azure CLI with Bicep, and an authenticated Azure account authorized to create
resources and Cosmos role assignments. Configure the selected external identity:

| Selection | Script parameters |
| --- | --- |
| JWT/OIDC | HTTPS `-AuthenticationAuthority`, non-empty `-AuthenticationAudience` |
| Entra | GUID `-AuthenticationTenantId`, `-AuthenticationClientId` |
| OTLP | Optional `-OtlpEndpoint`, `-OtlpProtocol grpc` or `http/protobuf` |
| PostgreSQL | Secure administrator password plus a **different** application login/password |
| SQL Server | Entra `-SqlAdminObjectId`, `-SqlAdminLogin`, optionally principal type; defaults to signed-in user |

Azure Monitor's destination is provisioned and injected when selected. OTLP must point to a collector
reachable from App Service; the local Aspire/dashboard endpoint is not a production destination.

Entra expects v2 API access tokens from `https://login.microsoftonline.com/<tenant-id>/v2.0`.
`-AuthenticationClientId` is the API application's client-ID GUID and the expected token `aud`, not the
calling client's ID or an `api://...` audience. Set the API registration's `api.requestedAccessTokenVersion`
to `2`; requested scopes may still use `api://<client-id>/...`. Tokens must contain `oid`.

Run from `deploy`. For a JWT/PostgreSQL profile:

```powershell
$adminPassword = Read-Host 'Database administrator password' -AsSecureString
$appPassword = Read-Host 'Separate runtime role password' -AsSecureString
.\deploy.ps1 -AuthenticationAuthority 'https://issuer.example.com' -AuthenticationAudience 'api' `
    -PostgresAdministratorPassword $adminPassword -PostgresApplicationLogin 'todo_runtime' `
    -PostgresApplicationPassword $appPassword -WhatIf
```

Remove `-WhatIf` to provision. For Entra, replace the authentication arguments with its tenant/client
GUIDs. For SQL Server, omit PostgreSQL parameters. Temporary ARM parameter files carry credentials
rather than command-line arguments; the script removes them and restricts their directory on Unix.
Run only on a trusted deployment host. Do not commit credential values or deployment logs.

**WhatIf still creates resource groups.** It previews their resources without applying them. The
regional preview uses `preview.invalid` for a not-yet-provisioned database endpoint; that placeholder
is never used for a real deployment. `-SkipGlobal` reads an existing `global` deployment's outputs.
Edit the `$Regions` list to change the regional footprint; keep existing short tokens stable.

## Bootstrap the database before publishing

Provisioning does not deploy application code, create runtime database users, apply migrations, or
seed production data. Keep schema deployment and runtime privileges separate.

For **SQL Server**, connect as the Entra administrator. Create a database user for each regional
managed identity (`CREATE USER [...] FROM EXTERNAL PROVIDER`) and grant only the needed data access.
Use a separate schema-deployment principal for DDL; runtime identities must not be administrators.
The generated runtime connection uses `Authentication=Active Directory Default` and the regional
user-assigned identity's client ID.

For **PostgreSQL**, connect as the administrator to the returned FQDN. Create the supplied application
login with its separate password and no superuser/role/database-creation privileges. Apply migrations
as a separate schema owner, then grant the runtime role CONNECT, schema USAGE, required table
SELECT/INSERT/UPDATE/DELETE, and sequence USAGE/SELECT. Set corresponding default privileges for
future migrations. The app receives only this runtime credential and uses `SSL Mode=VerifyFull`;
the administrator password is never injected into App Service.

Generate migrations for the **selected** EF provider; do not reuse SQLite migrations for a server
provider. The EF design-time startup needs Development configuration and the selected connection
string. Use your pinned EF tooling with `Acl\src\AntiCorruptionLayer.csproj` and startup
`Api\src\Api.csproj`, then apply the migration/script using the schema-deployment credential.

Cosmos is provisioned with `/scope`, `defaultTtl: -1`, disabled account-key auth, and native
data-contributor access scoped to the idempotency container for each app identity.

## Publish and harden

Publish `Api\src\Api.csproj` in Release and deploy its output to every regional App Service
(for example, a ZIP with `az webapp deploy`). App Service runs .NET 10 with HTTPS-only ingress.
Validate external authentication, database access, Cosmos, and exporter destinations before routing
traffic. Development actors and automatic schema creation are disabled outside Development.

**Never run a deployed application in Development.** The scaffold sets `ASPNETCORE_ENVIRONMENT=Production`;
ensure `DOTNET_ENVIRONMENT` is unset or also `Production` and do not override these through deployment
settings. Development skips external JWT validation and endpoint authentication requirements,
accepts test actors, and creates the sample schema. Release compilation does not select the runtime environment.

This is a bootstrap scaffold, not a complete private-network or disaster-recovery design.
Database firewalls permit Azure services broadly; replace that rule with private endpoints/VNet
integration or explicit trusted egress before production. Review database capacity, backups,
regional data availability, secret storage/rotation, and access policies for your workload.
Never put runtime traffic on a database administrator credential.

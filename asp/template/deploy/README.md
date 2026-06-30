# Multi-region Azure deployment

A worked example of deploying this service to **two Azure regions** where every resource name comes
from the [Trellis Azure naming convention](../Acl/tests/ResourceNamingLibraryTests.cs)
(`Trellis.ResourceNaming.Azure`).

The point: the **same** library the service uses at runtime computes the names here, so the running
service and the infrastructure agree on every name — including the deterministic global-uniqueness
suffix that Bicep's `uniqueString()` cannot reproduce. No name is ever invented in Bicep or
PowerShell.

```
deploy/names  (C#)              infra/*.bicep            Azure
DeployedEnvironmentOptions  ->  parameters          ->  named resources
        |                                                     ^
        +----------------- same names at runtime -------------+
```

## Two stacks, dictated by the convention

The convention assigns each resource type either a **region-less** name (a cloud-singleton) or a
**region-bearing** name. That split is the deployment topology:

| Stack | When | Resources | Example name |
|---|---|---|---|
| **Global** | once per cloud | SQL server, SQL database | `tdo-sql-prod-nhm4y`, `tdo-sqldb-prod` |
| **Regional** | once per region | Managed identity, Log Analytics, App Service | `tdo-app-prod-usw3-5yqp9`, `tdo-id-prod-usw3` |

Because the singleton names are identical in every region, every region's app connects to the **same**
SQL server, and re-running a later region never recreates it. Because the regional names carry the
region token, each region gets its own identity/workspace/app. Deploying "one by one" is then
stateless: each wave just rebinds the region and recomputes — there is nothing to remember between
waves.

> **No Key Vault?** The sample is passwordless — SQL uses the app's managed identity
> (`Authentication=Active Directory Default`), idempotency is in-memory, and auth is Entra/OIDC, so
> there is no secret to store. Add a Key Vault (a `tdo-kv-prod-<region-short>-<hash>` regional resource)
> only when your service has a secret that cannot use managed identity — a third-party API key, a
> signing/TLS certificate, or a credential for a dependency that does not support Entra auth.

> **Data tier note.** The convention currently models SQL as a single cloud-singleton (one writable
> server). For active-active per-region SQL you would add a failover-group alias — tracked as a future
> enhancement, out of scope for this example.

## Prerequisites

- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) (`az login` completed)
- The Bicep CLI (`az bicep install`)
- .NET SDK 10 (to run the names tool)
- An Azure subscription you can create resource groups in, and rights to make yourself the SQL
  Microsoft Entra administrator

## Run it

```powershell
# Preview everything first — no changes are made to deployments.
./deploy.ps1 -WhatIf

# Provision the global stack, then each region in turn.
./deploy.ps1
```

Useful switches:

| Switch / parameter | Purpose |
|---|---|
| `-WhatIf` | Preview each deployment without applying it (the resource groups are still created so the previews can run). |
| `-SkipGlobal` | Re-deploy only the regional waves (singletons already exist). |
| `-System`, `-Environment`, `-Cloud`, `-Scope` | Override the deployment context (defaults: `tdo` / `prod` / `AzureCloud` / `Shared`). |
| `-PrimaryRegion` | Region that homes the global resource group (default `westus3`). |
| `-SqlAdminObjectId`, `-SqlAdminLogin` | Entra SQL administrator (defaults to the signed-in user). |

### Add or remove a region

Edit the one list at the top of [`deploy.ps1`](./deploy.ps1):

```powershell
$Regions = @(
    [pscustomobject]@{ Name = 'westus3'; Short = 'usw3' }
    [pscustomobject]@{ Name = 'eastus2'; Short = 'use2' }
)
```

`Short` is the region token that appears in the resource names (keep it stable — it is part of the
durable name).

## What this provisions — and what it does not

`deploy.ps1` **provisions and configures the infrastructure**: the resource groups, SQL server +
database (Entra-only auth), and per region a managed identity, Log Analytics workspace, and an App
Service wired with the `DeployedEnvironment:*` settings and a passwordless SQL connection string.

To actually **serve traffic**, the sample needs a few app-side steps it intentionally leaves to you
(it ships SQLite + a development actor provider for zero-setup local dev):

1. **Grant each region's managed identity a database user** (a SQL data-plane step Bicep cannot do):
   ```sql
   CREATE USER [tdo-id-prod-usw3] FROM EXTERNAL PROVIDER;
   ALTER ROLE db_datareader ADD MEMBER [tdo-id-prod-usw3];
   ALTER ROLE db_datawriter ADD MEMBER [tdo-id-prod-usw3];
   ```
2. **Switch the Acl from the SQLite provider to the SqlServer provider** so the app uses the
   provisioned Azure SQL (the connection string is already injected as `ConnectionStrings:DefaultConnection`).
3. **Register a production `IActorProvider`** (e.g. `AddEntraActorProvider`) — the sample throws in
   non-Development environments by design until you do.
4. **Publish the app** to each region's App Service (e.g. `az webapp deploy`), or wire CI to do so.

## Files

| File | Role |
|---|---|
| [`names/`](./names) | C# tool: `DeployedEnvironmentOptions` → resource-name JSON (the C# → IaC seam). |
| [`deploy.ps1`](./deploy.ps1) | Orchestrates the global stack, then each regional wave. |
| [`../infra/global.bicep`](../infra/global.bicep) | Cloud-singleton resources (SQL). |
| [`../infra/regional.bicep`](../infra/regional.bicep) | Per-region resources (identity, Log Analytics, App Service). |

# Azure Container Apps deployment

Generated only for `--deployment azure` with an explicit `--database sqlserver` or `postgres`.
`deploy\names` computes Azure names through the shared Trellis convention and derives the Service Bus
topic from the actual integration-event contract. HTTP API versioning does not rename that topic.

## Foundation first

Use PowerShell 7, .NET 10, Azure CLI/Bicep, and an Azure identity authorized to provision resources and
Cosmos role assignments. Run from `deploy`:

```powershell
$adminPassword = Read-Host 'Database administrator password' -AsSecureString
.\deploy.ps1 -DatabaseAdministratorLogin 'trellisadmin' `
    -DatabaseAdministratorPassword $adminPassword -FoundationOnly
```

This creates the selected server and separate Members/Projects databases, Service Bus topic/subscription,
Cosmos idempotency store, user-assigned identities, Log Analytics, optional Application Insights, and a
Container Apps environment. It does **not** start applications. Save the non-secret output FQDNs and
gateway URL. Names are deterministic; use the same system/environment/cloud/region arguments for rollout.

For PostgreSQL, the scaffold uses Flexible Server 16. For SQL Server, it uses password-authenticated
administrator bootstrap. In both cases, create **separate least-privilege runtime users** for Members and
Projects. Never use the administrator credential as a service's connection string.

Generate and apply migrations for the selected provider using a separate schema-deployment credential.
The startup projects are `Members\Api\src\Members.Api.csproj` and `Projects\Api\src\Projects.Api.csproj`;
their EF projects are the corresponding `Acl\src` projects. EF design-time startup needs Development
configuration, the selected connections, and messaging configuration. Keep runtime privileges to the
required table/sequence access; do not grant DDL or access to the other service's database.
The sample's Development `EnsureCreated` and seeding are not production migration mechanisms.

## Images and application rollout

Build each Dockerfile from the solution root. Publish all three images to your registry; use immutable
digest references for repeatable deployments. For a private Azure Container Registry, grant **AcrPull**
to each of the three provisioned identities and pass `-RegistryServer`. Registry role grants are an
explicit prerequisite, not inferred from an arbitrary image hostname.

Provide secure runtime connection strings, the selected external identity, and a persistent RSA PEM
private key of at least 2048 bits:

```powershell
$membersConnection = Read-Host 'Members runtime connection string' -AsSecureString
$projectsConnection = Read-Host 'Projects runtime connection string' -AsSecureString
.\deploy.ps1 -DatabaseAdministratorLogin 'trellisadmin' -DatabaseAdministratorPassword $adminPassword `
    -MembersDatabaseConnectionString $membersConnection -ProjectsDatabaseConnectionString $projectsConnection `
    -GatewayImage '<registry>/gateway@sha256:<digest>' -MembersImage '<registry>/members@sha256:<digest>' `
    -ProjectsImage '<registry>/projects@sha256:<digest>' -RegistryServer '<registry>' `
    -SigningKeyPath '<persistent-private-key.pem>' `
    -AuthenticationAuthority 'https://issuer.example.com' -AuthenticationAudience 'api'
```

For Entra profiles, replace the authority/audience arguments with GUID `-AuthenticationTenantId` and
`-AuthenticationClientId`. Optionally pass a reachable `-OtlpEndpoint` and its protocol. Application
Insights configuration is provisioned/injected only when selected.

The application deployment reuses the foundation and wires HTTPS gateway ingress, internal-only service
ingress/discovery, the same public HTTPS `Gateway:Issuer` in all hosts, Cosmos identity access for Members,
and per-service messaging credentials. Members receives topic Send access; Projects receives Listen
access. The subscription filters the actual contract Subject and disables the default catch-all rule.
Both workers retain at least one replica so outbox/inbox work continues without inbound HTTP traffic.

The active private key is a persisted Container Apps secret mounted at `/keys/active.pem`, not an
ephemeral key generated per replica. Temporary ARM files carry secrets instead of command-line values,
are restricted on Unix, and are removed after deployment. Run on a trusted deployment host.
Do not commit keys, connection strings, parameters, or logs.

## Rotation and preview

`-PublishedKeyPaths @{ next = '<next-public.pem>'; previous = '<previous-public.pem>' }` publishes
additional **public-only** keys in JWKS. Pre-publish the future key before changing the active signer;
retain the old public key until existing tokens and downstream JWKS caches have aged out. Each public
key must be distinct, including from the active signer. Keep the active private key backed up securely.
Key IDs are deterministic SHA256 hashes of public-key material, so restarts preserve them.
Published files are mounted at `/keys/published-<alias>.pem`, separate from the private signer's
`/keys/active.pem`, even when a published alias is named `active`.

`-WhatIf` creates the resource group and previews resources without deploying them.
`-FoundationOnly -WhatIf` previews bootstrap without requiring application configuration.
Direct Bicep defaults to `deployApplications=false`; use the script for preflight and rollout.

## Production hardening

This scaffold does not configure private networking, private-registry grants, database schema/users,
regional failover, secret rotation automation, or a custom domain. Its database firewalls allow Azure
services broadly. Replace those rules with private endpoints or trusted egress; review capacities,
backups, availability, and access policies before serving production traffic.
Production requires external tokens; `X-Test-Actor` is Development-only.

# Infrastructure Resource Naming Convention (CAF-aligned)

## The bar
Massive systems (Microsoft / Amazon scale): many subscriptions, regions, scale-units/stamps/cells,
environments **and** safe-deploy rings, per-cloud naming rules, cost/governance at scale.
Deterministic (idempotent IaC) and human-greppable.

## Deployment model (terminology locked)
**Cloud ⊃ Region.** US / EU / SG are separate **sovereign/dedicated clouds** — own Entra, own DNS,
own control plane, fully isolated (no cross-cloud data sharing). Within a cloud are physical
**regions** (West / Central) for HA, which share that cloud's DNS namespace.
- **Cloud** = the isolation boundary → **Isolated** CloudScope. It drives the endpoint DNS suffix and
  is a **tag**, NOT a name token (everything in the EU cloud is implicitly EU). Exactly how the ASP
  template's `EnvironmentOptions.Cloud` works (`vault.azure.net` vs `vault.usgovcloudapi.net` …).
- **Region** = a **name token** for *regional* resources (one per region within a cloud), because the
  cloud's DNS namespace spans its regions. Cloud-singleton resources (one per cloud) omit it. This is
  the baseline's `GetRegionalResourceName` (region in name) vs `GetSharedResourceName` (no region).
- *Extension for the microservices template:* the `CloudType` enum + endpoint-suffix map gains the
  team's sovereign clouds (US/EU/SG) alongside the stock `AzureCloud`/`USGov`/`China`.

## CloudScope — two modes, default = Isolated
- **Isolated** (default): air-gapped / Azure Stack Hub / dedicated sovereign / single-tenant stamp.
  DNS namespaces are your own → **no global suffix**. Region-scoped tokens carry uniqueness.
- **Shared**: commercial or shared sovereign Azure. The types CAF marks **Global** name-scope
  (st, kv, sbns, evhns, cosmos, sql, app, cr — PaaS with a public DNS name) compete provider-wide →
  append a 5-char deterministic suffix `{u5}` to **those types only**. `{u5}` is a base36 (a–z0–9)
  encoding of a SHA-256 hash of the resource's logical identity (its name tokens) plus the cloud, so the
  same workload yields a stable suffix while distinct workloads diverge. CAF's other scopes (Resource
  group, Resource) need no suffix. Non-DNS types stay hashless in every mode.

## Naming context (inputs)
```
system,    // product short code        2–6   "ptk"
service?,  // bounded context           2–6   "mbr","prj"  (omit => system-shared)
role?,     // role of THIS resource -> the 'purpose' TAG, NEVER a name token   "blob","ehcheckpoint","app"
env,       // CAF word: local|test|stage|prod (auto 1-char l/t/s/p fallback on overflow)
region?,   // physical region within a cloud  "wus","cus","weu"  (in name for REGIONAL resources only)
stamp?,    // IMMUTABLE cell/scale-unit ordinal  "001"
instance?, // disambiguates multiple same-TYPE resources in a slice (role differs -> tag)  "001"
cloud,     // sovereign cloud US|EU|SG (CloudType) -> endpoint suffix + tag, NOT a name token
scope      // Isolated | Shared
```
Tags carry the FULL taxonomy regardless of name:
`system, service, purpose, env, region, stamp, ring, zone, instance, owner, costCenter, dataClassification`.

## Pattern (workload FIRST — so the portal's name sort groups by service)
```
{system}[-{service}]-{type}-{env}[-{region}][-{stamp}][-{instance}][-{u5 if Shared & DNS-named}]
```
- **Lead with `{system}-{service}`** — a **fixed platform profile, not per-deployment** — so a name
  sort groups all of a service's resources together (`ptk-mbr-*`). Type is the 3rd token; the Azure
  portal already facets by Type, so the name doesn't need to.
- After the workload+type prefix, the rest is **stable → volatile** (env → region → stamp → instance
  → suffix).
- Charset-restricted types (**Storage, ACR** — truly dashless) use the condensed (no-dash, lowercased)
  form. Cosmos/Key Vault/SB/EH/SQL/App **allow hyphens — keep them dashed** so they sort with the
  service block (dashless types form a second, also service-ordered, block).

## Length & charset — FAIL, don't truncate
- Every token has a fixed max-length budget. The resolver **validates and throws** if a required
  token would overflow; it NEVER silently drops or truncates a disambiguating dimension (that would
  collide two distinct resources onto one name).
- Author fixes overflow by shortening the 2–6 char system/service CODES (they exist for
  exactly this) — not by mutating the algorithm.
- In Shared scope, length-capped DNS types reserve room for `{u5}` up front.

## Sort order & grouping (hierarchical — don't overload one name)
1. **Management group / subscription** — coarsest split (per org/BU, per env, and here per region).
2. **Resource group = the primary deployment slice**, workload-first and **includes region**:
   `rg-{system}[-{service}]-{env}-{region}[-{stamp}]` → `rg-ptk-mbr-prod-weu-001` (the universal
   `rg-` prefix stays; everything after it is workload-ordered so a service's RGs cluster).
3. **Resource name = workload-first** (`{system}-{service}-{type}-…`) so a name sort groups all of a
   service's resources together — the portal already gives type-grouping for free via the Type facet,
   so the name serves the grouping it doesn't: by service.
4. **Type-scoped views** (e.g. the Storage blade) still group well — already filtered to the type,
   they sort by the next tokens = workload, so each service's accounts cluster.
5. **Tags = arbitrary grouping** (cost, governance) — reliable ONLY if **enforced by Azure Policy**:
   require `system, service, env, region, stamp, owner, costCenter, dataClassification`; IaC modules
   stamp tags on every resource, not just the RG.

## Examples — **EU cloud** (implicit: drives the endpoint suffix + a `cloud=eu` tag, never in the name),
West Europe = `weu`. Isolated scope (no `{u5}`); env is the full CAF word, 1-char only on overflow.
Workload-first `{system}-{service}-{type}-…` so a name sort clusters all `ptk-mbr-*` together.
Whether a type is *regional* vs *cloud-singleton* is the team's per-resource call.

**Cloud-singleton** (one per cloud — **no** region token):
| Resource | Name |
|---|---|
| Resource group (system slice) | `rg-ptk-prod` |
| Service Bus namespace (alias) | `ptk-sbns-prod` |
| SQL logical server | `ptk-sql-prod` |
| SQL database | `ptk-mbr-sqldb-prod` |
| Storage — shared blob (condensed; role=blob in tag) | `ptkmbrstprod` |
| Container Registry | `ptkcrprod` |
| Log Analytics | `ptk-log-prod` |

**Regional** (one per region within the cloud — region token **included**):
| Resource | Name |
|---|---|
| Resource group (service slice) | `rg-ptk-mbr-prod-weu-001` |
| Key Vault | `ptk-mbr-kv-prod-weu` |
| App Service | `ptk-mbr-app-prod-weu` |
| Managed identity — app / deploy (roles in tags) | `ptk-mbr-id-prod-weu-001` / `ptk-mbr-id-prod-weu-002` |

A name sort now clusters every `ptk-mbr-*` resource together. The same service in the **US cloud**,
Central US (`cus`), is identical but `…-cus`, in a physically separate cloud — names may even
**repeat across clouds** (separate DNS namespaces).

**Shared** scope (commercial Azure — the template's other users) adds `{u5}` to the Global-scope
types: `ptk-sbns-prod-k7x2q`, `ptkmbrstprodk7x2q`, `ptk-mbr-kv-prod-weu-k7x2q`. **Length
fallback:** if a name overflows (Storage at 24), env drops to `p`; if still over, shorten the
system/service codes — never truncate.

### Same type, two roles — the Storage + Event Hubs case
A single resource TYPE can serve two roles in one slice — e.g. a shared blob store **and** a per-region
Event Hub checkpoint store. The role is **not** a name token (Storage has no room for one — see the
budget below); it lives in the `purpose` **tag**. The names stay distinct because the two roles differ
in **scope**, and any same-scope duplicates use `{instance}`:
| Role (→ `purpose` tag) | Scope | Name |
|---|---|---|
| `blob` (shared) | cloud-singleton (no region) | `ptkmbrstprod` |
| `ehcheckpoint` | regional, one per region | `ptkmbrstprodweu`, `ptkmbrstprodneu`, … |

The checkpoint stores differ by region; the blob store carries no region token — so they never collide
without a role token. Two stores that *share* a scope are disambiguated by `{instance}`
(`ptkmbrstprod01`, `ptkmbrstprod02`), role in the tag. A shared-storage helper therefore needs a
regional sibling (one name per region) — not a role token.

### Storage budget — why role can't be a name token
Storage is the tightest namespace: **3–24 chars, lowercase alphanumeric, no dashes, globally unique**
(Shared scope). The budget is too tight to *rely* on a role token fitting, so role is never a Storage
name token:
```
ptk + mbr + st + prod + weu + 001 + 001    = 21   system+service+type+env+region+stamp+instance — fits
                              + role "blob"  = 25   — OVER 24
Shared scope: + {u5} hash (5)               = 26   — OVER even without a role token
```
Because you can't count on the room, role is a tag **everywhere** (one rule), and Storage disambiguates
same-scope duplicates with `{instance}`. Fitting names: `ptkmbrstprod` (Isolated),
`ptkmbrstpweuk7x2q` (Shared — env→`p`, `{u5}` hash).

### Geo-DR paired Service Bus / Event Hubs (the alias case)
SB Premium / Event Hubs geo-DR pairs **exactly two** regions (primary + secondary) behind a stable
**alias** that repoints on failover. The split falls straight out of shared-vs-regional:
| Role | Scope | Name | Used by |
|---|---|---|---|
| Alias (failover-stable endpoint) | cloud-singleton (no region) | `ptk-sbns-prod` | **services** (read/write) |
| Primary namespace | regional | `ptk-sbns-prod-weu` | provisioning / failover |
| Secondary namespace | regional | `ptk-sbns-prod-neu` | provisioning / failover |

**Connection rule:** the *shared* name **is** the service-facing connect identity. A service always
asks for `ptk-sbns-prod` and never learns whether that resolves to a single namespace (non-paired) or
a geo-DR alias (paired) — pairing is an infra detail behind the stable name.

**Lifecycle rule (avoid the alias trap):** physical namespaces are ALWAYS regional
(`ptk-sbns-prod-weu`), even in single-region mode. The stable connect name `ptk-sbns-prod` is a
*logical* identity the resolver maps to the regional endpoint until a Geo-DR alias exists. **Never
create a physical namespace literally named `ptk-sbns-prod`** — Azure then can't add an alias of the
same DNS identity, turning "pair it later" into a replace/migrate.

**DR primitives differ by family** — don't over-generalize the alias model:
- **Service Bus / Event Hubs** → alias + 2 regional namespaces (above).
- **SQL** → a failover-group *listener* (the stable connect name) + regional logical servers.
- **Cosmos** → usually ONE account whose global endpoint is the stable name; regions are account
  configuration, not separate accounts.

### Managed Identity — regional + user-assigned
MI is a **regional** resource: one **User-Assigned Managed Identity (UAMI)** per region per role (e.g.
app vs deploy). Multiple UAMIs in a region are disambiguated by `{instance}`, role in a tag —
`ptk-mbr-id-prod-weu-001`, `ptk-mbr-id-prod-weu-002`. Resources reference the regional UAMI (never
System-Assigned), so identities are pre-created and granted ahead of deployment and decoupled from
resource lifecycle. Each region's resources bind to that region's UAMI.

> **Cardinality is cataloged, not inferred from type.** Whether a resource is cloud-singleton vs
> regional, and whether it needs `stamp`/`instance`, is declared per resource — not guessed from the
> type. `{instance}` is the same-type disambiguator: include it for any type that can have >1 of itself
> sharing a scope in a slice (role in the `purpose` tag); cataloged true singletons omit it. (Some
> "singletons" — Log Analytics, SQL server, App Service Plan — are physically regional; omit region
> only when there is exactly one logical instance per cloud.)

## Per-resource adapters (abbrev + rules pinned to CAF; "DNS-global" = CAF "Global" name scope)
| Type | Abbr | Charset | Len | Sep | DNS-global |
|---|---|---|---|---|---|
| Storage account | st | a–z0–9 | 3–24 | none | yes |
| Key Vault | kv | a–z0–9-, start/end alnum, no `--` | 3–24 | dash | yes |
| Service Bus ns | sbns | a–z0–9-, start alpha / end alnum | 6–50 | dash | yes |
| Event Hubs ns | evhns | a–z0–9- | 6–50 | dash | yes |
| Cosmos account | cosmos | a–z0–9- | 3–44 | dash | yes |
| SQL logical server | sql | a–z0–9- | 1–63 | dash | yes |
| SQL database | sqldb | most | 1–128 | dash | no |
| App Service | app | a–z0–9- | 2–60 | dash | yes (hostname) |
| Container Registry | cr | a–z0–9 | 5–50 | none | yes |
| Managed Identity | id | a–z0–9-_ | 3–128 | dash | no |
| Log Analytics | log | a–z0–9- | 4–63 | dash | no |
| App Insights | appi | most | 1–260 | dash | no |
| Resource group | rg | a–z0–9-_(). | 1–90 | dash | no |

Abbreviations + length/charset rules are pinned to the CAF official sources — the
**resource-abbreviations** list and **resource-name-rules** — and the Azure Naming Tool
(github.com/Azure/AzureNamingTool) encodes the same convention. Cloud endpoint suffixes mapped per
`CloudType` as the ASP template (`*.blob.core.windows.net` / `*.servicebus.windows.net` /
`*.vault.azure.net` / sovereign variants). (Baseline `cosno`→`cosmos`; added ACR.)

## env vs ring (separate concerns)
- **env** (lifecycle) → CAF full word by default for readability (`local`, `test`, `stage`, `prod`);
  automatic per-name 1-char fallback (`l`, `t`, `s`, `p`) applied ONLY when a name would exceed its
  type's length limit (CAF sanctions this length-driven inconsistency). Map long baseline
  `EnvironmentType` values (`test`/`ppe`/`prod`/`local`) onto these.
- **ring** (safe-deploy: `r0/r1/canary/pilot`) → **tag always**; name token only when multiple rings
  coexist as separate resources in one subscription/RG/stamp.

## Tags-only dimensions (never in most names)
- **ring**, **zone** (AZ — name token only for zonal VM/NIC/disk/public-IP), **dataClassification**
  (`public|internal|confidential|restricted`; split into separate subscriptions/landing-zones only
  if governance requires), **deploymentSlot/color** (App Service → platform slots; tag for full
  parallel blue/green stamps).

## Stamp / scale-unit
`stamp` = the **immutable** cell / scale-unit / blast-radius boundary. Stable ordinal (`001`,
`002`, or base36 `a01`). **Decide at creation:** if a workload is ever stamp-capable, always include
`stamp` (`001`) so scaling out later does NOT rename (= replace) existing resources. Same rule for
`region` if a subscription might ever host more than one.

## Where it lives
A small shared library referenced by **every service and the Aspire AppHost**, so the AppHost
provisions with the exact names each service consumes. Mirrors the ASP template's `EnvironmentOptions`
+ per-type extension methods, extended with: CloudScope (Isolated/Shared), `{instance}` disambiguation,
fail-on-overflow validation, and Azure-Policy tag enforcement.

## Key design choices
RG system-first + resource type-first; tags as the full taxonomy; per-resource adapters (Azure rules
are too inconsistent for one generic formatter); region-as-container for independent regional stacks.

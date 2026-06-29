<#
.SYNOPSIS
    Provisions the convention-named Azure resources for this service across one or more regions.

.DESCRIPTION
    Demonstrates how to deploy a Trellis service to multiple Azure regions where EVERY resource name
    comes from the Trellis.ResourceNaming.Azure convention — so the running service and the
    infrastructure agree on every name by construction.

    The deployment has two stacks, dictated by the convention:

      * Global  (deployed ONCE)        : cloud-singletons with region-less names — the SQL server and
                                         database. Both regions connect to the SAME server.
      * Regional(deployed PER REGION)  : resources whose names carry the region token — the managed
                                         identity, Key Vault, Log Analytics workspace, and App Service.

    The names are computed by the C# convention (deploy/names) and passed into Bicep as parameters;
    no name is invented in Bicep or PowerShell. Re-running is safe: the global names are identical
    every wave, so later waves reference the existing singletons instead of recreating them.

    This script PROVISIONS and CONFIGURES infrastructure. Running the service in production also
    requires the app-side steps documented in deploy/README.md (SqlServer EF provider, an Entra
    actor provider, and a database user for each region's managed identity).

.EXAMPLE
    ./deploy.ps1 -WhatIf
    Preview every deployment without changing anything.

.EXAMPLE
    ./deploy.ps1
    Provision the global stack, then each region in turn.
#>
[CmdletBinding()]
param(
    [string] $System = 'tdo',
    [string] $Environment = 'prod',
    [string] $Cloud = 'AzureCloud',
    [ValidateSet('Shared', 'Isolated')]
    [string] $Scope = 'Shared',

    # The region where the global resources' resource group is homed. The global resources themselves
    # are region-less by name; their RG still needs a location for metadata.
    [string] $PrimaryRegion = 'westus3',

    # Microsoft Entra administrator for the SQL server (Entra-only auth, no SQL passwords). Defaults
    # to the signed-in user.
    [string] $SqlAdminObjectId,
    [string] $SqlAdminLogin,
    [ValidateSet('User', 'Group', 'Application')]
    [string] $SqlAdminPrincipalType = 'User',

    [string] $SubscriptionId,

    # Preview every deployment (az deployment ... --what-if) without changing anything.
    [switch] $WhatIf,

    # Skip the global stack (use when re-deploying only the regional waves).
    [switch] $SkipGlobal
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Add or remove a region here — that is the only edit needed to change the deployment footprint.
$Regions = @(
    [pscustomobject]@{ Name = 'westus3'; Short = 'usw3' }
    [pscustomobject]@{ Name = 'eastus2'; Short = 'use2' }
)

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$namesProject = Join-Path $here 'names'
$infra = Join-Path (Split-Path -Parent $here) 'infra'
$workDir = Join-Path ([System.IO.Path]::GetTempPath()) "trellis-names-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force -Path $workDir | Out-Null

function Invoke-Az {
    param([Parameter(ValueFromRemainingArguments)] [string[]] $Arguments)
    & az @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "az $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

# Computes the convention names for a stack by running the SAME library the service uses at runtime.
function Get-Names {
    param([string[]] $ExtraArgs = @())

    $outFile = Join-Path $workDir "names-$([guid]::NewGuid().ToString('N')).json"
    $common = @('--system', $System, '--environment', $Environment, '--cloud', $Cloud, '--scope', $Scope)
    dotnet run --project $namesProject -c Release --no-build -- @common @ExtraArgs --out $outFile *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "names tool failed (args: $($ExtraArgs -join ' '))"
    }

    return Get-Content -Raw -Path $outFile | ConvertFrom-Json
}

try {
    # --- Preflight ---------------------------------------------------------------------------------
    Invoke-Az account show --output none
    if ($SubscriptionId) {
        Invoke-Az account set --subscription $SubscriptionId
    }

    if (-not $SqlAdminObjectId) {
        Write-Host 'Resolving the signed-in user as the SQL Entra administrator...'
        $me = az ad signed-in-user show | ConvertFrom-Json
        $SqlAdminObjectId = $me.id
        if (-not $SqlAdminLogin) { $SqlAdminLogin = $me.userPrincipalName }
    }

    # The SQL server's Entra administrator block requires both a sid and a login; a bare object id
    # would pass an empty login to Bicep and fail the deployment.
    if (-not $SqlAdminLogin) {
        throw 'Provide -SqlAdminLogin (the Entra administrator display name / UPN) together with -SqlAdminObjectId.'
    }

    Write-Host "Building the names tool ($namesProject)..."
    dotnet build $namesProject -c Release --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Failed to build the names tool.' }

    $deployMode = @()
    if ($WhatIf) {
        $deployMode = @('--what-if')
        # Group-scoped what-if needs the target resource group to exist, so the (free, idempotent)
        # resource groups are still created; no other resource is deployed in this mode.
        Write-Host 'WhatIf: resource groups are created so deployments can be previewed; nothing else is changed.'
    }

    # --- Global stack (once) -----------------------------------------------------------------------
    $global = Get-Names
    if (-not $SkipGlobal) {
        Write-Host "`n=== Global stack -> $($global.globalResourceGroup) ($PrimaryRegion) ==="
        Invoke-Az group create --name $global.globalResourceGroup --location $PrimaryRegion --output none
        $globalArgs = @('deployment', 'group', 'create',
            '--resource-group', $global.globalResourceGroup,
            '--template-file', (Join-Path $infra 'global.bicep'),
            '--parameters',
            "location=$PrimaryRegion",
            "sqlServerName=$($global.sqlServerName)",
            "sqlDatabaseName=$($global.sqlDatabaseName)",
            "sqlAdminObjectId=$SqlAdminObjectId",
            "sqlAdminLogin=$SqlAdminLogin",
            "sqlAdminPrincipalType=$SqlAdminPrincipalType") + $deployMode
        Invoke-Az @globalArgs
    }
    else {
        Write-Host "Skipping global stack (referencing existing $($global.sqlServerName))."
    }

    # --- Regional stacks (one by one) --------------------------------------------------------------
    $summary = @()
    foreach ($region in $Regions) {
        $names = Get-Names @('--region', $region.Name, '--region-short', $region.Short)
        Write-Host "`n=== Region $($region.Name) -> $($names.resourceGroup) ==="
        Invoke-Az group create --name $names.resourceGroup --location $region.Name --output none
        $regionalArgs = @('deployment', 'group', 'create',
            '--resource-group', $names.resourceGroup,
            '--template-file', (Join-Path $infra 'regional.bicep'),
            '--parameters',
            "location=$($region.Name)",
            "appServiceName=$($names.appServiceName)",
            "managedIdentityName=$($names.managedIdentityName)",
            "keyVaultName=$($names.keyVaultName)",
            "logAnalyticsName=$($names.logAnalyticsName)",
            "sqlServerFqdn=$($global.sqlServerFqdn)",
            "sqlDatabaseName=$($global.sqlDatabaseName)",
            "deployedSystem=$System",
            "deployedEnvironment=$Environment",
            "deployedCloud=$Cloud",
            "deployedRegion=$($region.Name)",
            "deployedRegionShortName=$($region.Short)",
            "deployedScope=$Scope") + $deployMode
        Invoke-Az @regionalArgs

        $summary += [pscustomobject]@{
            Region        = $region.Name
            ResourceGroup = $names.resourceGroup
            AppService    = $names.appServiceName
        }
    }

    if (-not $WhatIf) {
        Write-Host "`n=== Provisioned ==="
        $summary | Format-Table -AutoSize | Out-String | Write-Host
        Write-Host 'Next steps to serve traffic (see deploy/README.md):'
        Write-Host '  1. Grant each region''s managed identity a SQL database user (data-plane step).'
        Write-Host '  2. Switch the Acl from the SQLite provider to the SqlServer provider.'
        Write-Host '  3. Register a production IActorProvider (e.g. AddEntraActorProvider).'
        Write-Host '  4. Publish the app to each region''s App Service (e.g. az webapp deploy).'
    }
}
finally {
    Remove-Item -Recurse -Force -Path $workDir -ErrorAction SilentlyContinue
}

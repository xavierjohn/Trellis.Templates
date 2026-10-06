<#
.SYNOPSIS
    Provisions the convention-named Azure resources for this service across one or more regions.

.DESCRIPTION
    Demonstrates how to deploy a Trellis service to multiple Azure regions where EVERY resource name
    comes from the Trellis.ResourceNaming.Azure convention — so the running service and the
    infrastructure agree on every name by construction.

    The deployment has two stacks, dictated by the convention:

      * Global  (deployed ONCE)        : cloud-singletons with region-less names — the database server and
                                         database, and Cosmos idempotency store. Regions share these.
      * Regional(deployed PER REGION)  : resources whose names carry the region token — the managed
                                         identity, Log Analytics, Application Insights, and App Service.

    The names are computed by the C# convention (deploy/names) and passed into Bicep as parameters;
    no name is invented in Bicep or PowerShell. Re-running is safe: the global names are identical
    every wave, so later waves reference the existing singletons instead of recreating them.

    This script provisions the selected database and App Service configuration. Apply database
    migrations and publish the application as described in deploy/README.md.

.EXAMPLE
    ./deploy.ps1 -WhatIf
    Preview deployments; creates resource groups but does not deploy their resources.

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
    [string] $AuthenticationAuthority,
    [string] $AuthenticationAudience,
    [string] $AuthenticationTenantId,
    [string] $AuthenticationClientId,
    [string] $OtlpEndpoint,
    [ValidateSet('grpc', 'http/protobuf')]
    [string] $OtlpProtocol = 'grpc',
    [string] $PostgresAdministratorLogin = 'trellisadmin',
    [SecureString] $PostgresAdministratorPassword,
    [string] $PostgresApplicationLogin,
    [SecureString] $PostgresApplicationPassword,

    # Resource groups are created even when previewing deployments.
    [switch] $WhatIf,

    # Skip the global stack (use when re-deploying only the regional waves).
    [switch] $SkipGlobal
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$databaseProvider = 'TEMPLATE_DATABASE_PROVIDER'
if ($databaseProvider -notin @('postgres', 'sqlserver')) {
    throw 'Azure deployment requires --database postgres or --database sqlserver. SQLite is not supported on Azure.'
}
if ('TEMPLATE_AUTH_PROVIDER' -eq 'entra') {
    $tenant = [Guid]::Empty
    $client = [Guid]::Empty
    if (![Guid]::TryParse($AuthenticationTenantId, [ref] $tenant) -or
        ![Guid]::TryParse($AuthenticationClientId, [ref] $client)) {
        throw 'Provide -AuthenticationTenantId and -AuthenticationClientId as Entra GUIDs.'
    }
} else {
    $authority = $null
    if (![Uri]::TryCreate($AuthenticationAuthority, [UriKind]::Absolute, [ref] $authority) -or
        $authority.Scheme -ne 'https' -or [string]::IsNullOrWhiteSpace($AuthenticationAudience)) {
        throw 'Provide an HTTPS -AuthenticationAuthority and a non-empty -AuthenticationAudience.'
    }
}
if ($OtlpEndpoint) {
    $endpoint = $null
    if (![Uri]::TryCreate($OtlpEndpoint, [UriKind]::Absolute, [ref] $endpoint) -or
        $endpoint.Scheme -notin @('http', 'https')) {
        throw '-OtlpEndpoint must be an absolute HTTP or HTTPS URL.'
    }
}
if ($databaseProvider -eq 'postgres') {
    if (!$SkipGlobal -and !$PostgresAdministratorPassword) {
        throw 'Provide -PostgresAdministratorPassword as a SecureString for the PostgreSQL Azure profile.'
    }
    if ([string]::IsNullOrWhiteSpace($PostgresApplicationLogin) -or !$PostgresApplicationPassword -or
        $PostgresApplicationLogin -eq $PostgresAdministratorLogin) {
        throw 'Provide a separate -PostgresApplicationLogin and -PostgresApplicationPassword for least-privilege runtime access.'
    }
}

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
if (!$IsWindows) {
    [IO.File]::SetUnixFileMode($workDir,
        [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute)
}

function Invoke-Az {
    param([Parameter(ValueFromRemainingArguments)] [string[]] $Arguments)
    & az @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "az $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

function Write-Parameters([hashtable] $Values) {
    $parameters = @{}
    foreach ($entry in $Values.GetEnumerator()) {
        $value = if ($entry.Value -is [SecureString]) {
            ConvertFrom-SecureString $entry.Value -AsPlainText
        } else { $entry.Value ?? '' }
        $parameters[$entry.Key] = @{ value = $value }
    }
    $path = Join-Path $workDir ("parameters-" + [guid]::NewGuid().ToString('N') + '.json')
    [IO.File]::WriteAllText($path, (@{ parameters = $parameters } | ConvertTo-Json -Depth 8),
        [Text.UTF8Encoding]::new($false))
    return "@$path"
}

# Computes the convention names for a stack by running the SAME library the service uses at runtime.
function Get-Names {
    param([string[]] $ExtraArgs = @())

    $outFile = Join-Path $workDir "names-$([guid]::NewGuid().ToString('N')).json"
    $common = @('--system', $System, '--environment', $Environment, '--cloud', $Cloud, '--scope', $Scope)
    # Capture every stream so a failure (missing SDK, restore/build error, bad argument) surfaces the
    # underlying message; on success the JSON is written to $outFile and the captured output is ignored.
    $output = dotnet run --project $namesProject -c Release --no-build -- @common @ExtraArgs --out $outFile 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "names tool failed (args: $($ExtraArgs -join ' ')):`n$($output | Out-String)"
    }

    return Get-Content -Raw -Path $outFile | ConvertFrom-Json
}

try {
    # --- Preflight ---------------------------------------------------------------------------------
    Invoke-Az account show --output none
    if ($SubscriptionId) {
        Invoke-Az account set --subscription $SubscriptionId
    }

    if ($databaseProvider -eq 'sqlserver' -and -not $SqlAdminObjectId) {
        Write-Host 'Resolving the signed-in user as the SQL Entra administrator...'
        $me = Invoke-Az ad signed-in-user show --output json | ConvertFrom-Json
        $SqlAdminObjectId = $me.id
        if (-not $SqlAdminLogin) { $SqlAdminLogin = $me.userPrincipalName }
    }

    # The SQL server's Entra administrator block requires both a sid and a login; a bare object id
    # would pass an empty login to Bicep and fail the deployment.
    if ($databaseProvider -eq 'sqlserver' -and -not $SqlAdminLogin) {
        throw 'Provide -SqlAdminLogin (the Entra administrator display name / UPN) together with -SqlAdminObjectId.'
    }

    Write-Host "Building the names tool ($namesProject)..."
    $buildOutput = dotnet build $namesProject -c Release --nologo 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to build the names tool:`n$($buildOutput | Out-String)"
    }

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
        $globalValues = @{
            location = $PrimaryRegion
            databaseServerName = $global.databaseServerName
            databaseName = $global.databaseName
            cosmosAccountName = $global.cosmosAccountName
            idempotencyDatabaseName = $global.idempotencyDatabaseName
        }
        if ($databaseProvider -eq 'postgres') {
            $globalValues.postgresAdministratorLogin = $PostgresAdministratorLogin
            $globalValues.postgresAdministratorPassword = $PostgresAdministratorPassword
        } else {
            $globalValues.sqlAdminObjectId = $SqlAdminObjectId
            $globalValues.sqlAdminLogin = $SqlAdminLogin
            $globalValues.sqlAdminPrincipalType = $SqlAdminPrincipalType
        }
        $globalArgs = @('deployment', 'group', 'create',
            '--resource-group', $global.globalResourceGroup,
            '--template-file', (Join-Path $infra 'global.bicep'),
            '--parameters', (Write-Parameters $globalValues)) + $deployMode
        Invoke-Az @globalArgs
    }
    else {
        Write-Host "Skipping global stack (referencing existing $($global.databaseServerName))."
    }
    $globalOutputs = if (!$WhatIf) {
        Invoke-Az deployment group show --resource-group $global.globalResourceGroup --name global `
            --query properties.outputs --output json | ConvertFrom-Json
    } else { $null }

    # --- Regional stacks (one by one) --------------------------------------------------------------
    $summary = @()
    foreach ($region in $Regions) {
        $names = Get-Names @('--region', $region.Name, '--region-short', $region.Short)
        Write-Host "`n=== Region $($region.Name) -> $($names.resourceGroup) ==="
        Invoke-Az group create --name $names.resourceGroup --location $region.Name --output none
        $regionalValues = @{
            location = $region.Name
            appServiceName = $names.appServiceName
            appServicePlanName = $names.appServicePlanName
            managedIdentityName = $names.managedIdentityName
            logAnalyticsName = $names.logAnalyticsName
            applicationInsightsName = $names.applicationInsightsName
            cosmosAccountName = $global.cosmosAccountName
            cosmosResourceGroupName = $global.globalResourceGroup
            cosmosEndpoint = $global.cosmosEndpoint
            idempotencyDatabaseName = $global.idempotencyDatabaseName
            databaseServerFqdn = $(if ($WhatIf) { 'preview.invalid' } else { $globalOutputs.databaseServerFqdn.value })
            databaseName = $global.databaseName
            authenticationAuthority = $AuthenticationAuthority
            authenticationAudience = $AuthenticationAudience
            authenticationTenantId = $AuthenticationTenantId
            authenticationClientId = $AuthenticationClientId
            otlpEndpoint = $OtlpEndpoint
            otlpProtocol = $OtlpProtocol
            deployedSystem = $System
            deployedEnvironment = $Environment
            deployedCloud = $Cloud
            deployedRegion = $region.Name
            deployedRegionShortName = $region.Short
            deployedScope = $Scope
        }
        if ($databaseProvider -eq 'postgres') {
            $regionalValues.postgresApplicationLogin = $PostgresApplicationLogin
            $regionalValues.postgresApplicationPassword = $PostgresApplicationPassword
        }
        $regionalArgs = @('deployment', 'group', 'create',
            '--resource-group', $names.resourceGroup,
            '--template-file', (Join-Path $infra 'regional.bicep'),
            '--parameters', (Write-Parameters $regionalValues)) + $deployMode
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
        Write-Host '  1. Grant the application least-privilege database access and apply EF migrations.'
        Write-Host '  2. Publish the app to each region''s App Service (e.g. az webapp deploy).'
    }
}
finally {
    Remove-Item -Recurse -Force -LiteralPath $workDir
}

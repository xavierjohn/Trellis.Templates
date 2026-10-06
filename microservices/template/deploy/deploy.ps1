[CmdletBinding()]
param(
    [string] $System = 'ptk',
    [string] $Environment = 'prod',
    [string] $Cloud = 'AzureCloud',
    [string] $Region = 'westus3',
    [string] $RegionShort = 'usw3',
    [Parameter(Mandatory)]
    [string] $DatabaseAdministratorLogin,
    [Parameter(Mandatory)]
    [SecureString] $DatabaseAdministratorPassword,
    [switch] $FoundationOnly,
    [string] $GatewayImage,
    [string] $MembersImage,
    [string] $ProjectsImage,
    [string] $RegistryServer,
    [string] $SigningKeyPath,
    [hashtable] $PublishedKeyPaths = @{},
    [SecureString] $MembersDatabaseConnectionString,
    [SecureString] $ProjectsDatabaseConnectionString,
    [string] $AuthenticationAuthority,
    [string] $AuthenticationAudience,
    [string] $AuthenticationTenantId,
    [string] $AuthenticationClientId,
    [string] $OtlpEndpoint,
    [ValidateSet('grpc', 'http/protobuf')]
    [string] $OtlpProtocol = 'grpc',
    [switch] $WhatIf
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ('TEMPLATE_DATABASE_PROVIDER' -notin @('postgres', 'sqlserver')) {
    throw 'Generate the Azure profile with an explicit --database postgres or --database sqlserver.'
}
if ([string]::IsNullOrWhiteSpace($DatabaseAdministratorLogin)) {
    throw '-DatabaseAdministratorLogin must not be empty.'
}
$privateKeyPem = ''
$publicKeys = @{}
if (!$FoundationOnly) {
    foreach ($image in @($GatewayImage, $MembersImage, $ProjectsImage)) {
        if ([string]::IsNullOrWhiteSpace($image)) { throw 'Provide all three image references before deploying applications.' }
    }
    if (!$MembersDatabaseConnectionString -or !$ProjectsDatabaseConnectionString) {
        throw 'Provide both least-privilege application database connection strings after applying the selected-provider migrations.'
    }
    if ([string]::IsNullOrWhiteSpace($SigningKeyPath)) {
        throw '-SigningKeyPath must identify persistent RSA private-key material.'
    }
    $privateKeyPem = Get-Content -LiteralPath (Resolve-Path -LiteralPath $SigningKeyPath).Path -Raw
    $rsa = [Security.Cryptography.RSA]::Create()
    try {
        $rsa.ImportFromPem($privateKeyPem)
        $null = $rsa.ExportParameters($true)
        if ($rsa.KeySize -lt 2048) { throw 'Gateway keys must be RSA with at least 2048 bits.' }
    } finally { $rsa.Dispose() }
    foreach ($entry in $PublishedKeyPaths.GetEnumerator()) {
        if ($entry.Key -notmatch '^[a-z0-9-]{1,20}$') { throw 'Published key names must use 1-20 lowercase letters, digits, or hyphens.' }
        $rsa = [Security.Cryptography.RSA]::Create()
        try {
            $rsa.ImportFromPem((Get-Content -LiteralPath (Resolve-Path -LiteralPath $entry.Value).Path -Raw))
            if ($rsa.KeySize -lt 2048) { throw 'Published gateway keys must be RSA with at least 2048 bits.' }
            $publicKeys[$entry.Key] = $rsa.ExportSubjectPublicKeyInfoPem()
        } finally { $rsa.Dispose() }
    }
    if ('TEMPLATE_AUTH_PROVIDER' -eq 'entra') {
        $tenant = [Guid]::Empty
        $client = [Guid]::Empty
        if (![Guid]::TryParse($AuthenticationTenantId, [ref] $tenant) -or
            ![Guid]::TryParse($AuthenticationClientId, [ref] $client)) {
            throw 'Provide valid -AuthenticationTenantId and -AuthenticationClientId GUIDs.'
        }
    } else {
        $authority = $null
        if (![Uri]::TryCreate($AuthenticationAuthority, [UriKind]::Absolute, [ref] $authority) -or
            $authority.Scheme -ne 'https' -or [string]::IsNullOrWhiteSpace($AuthenticationAudience)) {
            throw 'Provide an HTTPS -AuthenticationAuthority and non-empty -AuthenticationAudience.'
        }
    }
}
if ($OtlpEndpoint) {
    $endpoint = $null
    if (![Uri]::TryCreate($OtlpEndpoint, [UriKind]::Absolute, [ref] $endpoint) -or
        $endpoint.Scheme -notin @('http', 'https')) {
        throw '-OtlpEndpoint must be an absolute HTTP or HTTPS URL.'
    }
}

function Invoke-Az {
    param([Parameter(ValueFromRemainingArguments)] [string[]] $Arguments)
    & az @Arguments
    if ($LASTEXITCODE -ne 0) { throw "az failed with exit code $LASTEXITCODE." }
}

$namesProject = Join-Path $PSScriptRoot 'names'
dotnet build $namesProject -c Release --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Failed to build the convention naming helper.' }
$output = & dotnet run --project $namesProject -c Release --no-build -- $System $Environment $Cloud $Region $RegionShort
if ($LASTEXITCODE -ne 0) { throw 'Failed to compute the deployment resource names.' }
$names = $output -join "`n" | ConvertFrom-Json
$values = @{
    location = $Region
    regionShortName = $RegionShort
    deployedSystem = $System
    deployedEnvironment = $Environment
    deployedCloud = $Cloud
    names = $names
    databaseAdministratorLogin = $DatabaseAdministratorLogin
    databaseAdministratorPassword = $DatabaseAdministratorPassword
    deployApplications = !$FoundationOnly
    gatewayImage = $GatewayImage
    membersImage = $MembersImage
    projectsImage = $ProjectsImage
    registryServer = $RegistryServer
    gatewaySigningPrivateKeyPem = $privateKeyPem
    gatewayPublishedPublicKeys = $publicKeys
    membersDatabaseConnectionString = $MembersDatabaseConnectionString
    projectsDatabaseConnectionString = $ProjectsDatabaseConnectionString
    authenticationAuthority = $AuthenticationAuthority
    authenticationAudience = $AuthenticationAudience
    authenticationTenantId = $AuthenticationTenantId
    authenticationClientId = $AuthenticationClientId
    otlpEndpoint = $OtlpEndpoint
    otlpProtocol = $OtlpProtocol
}
$workDirectory = Join-Path ([IO.Path]::GetTempPath()) ('trellis-deploy-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workDirectory | Out-Null
if (!$IsWindows) {
    [IO.File]::SetUnixFileMode($workDirectory,
        [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute)
}
try {
    $parameters = @{}
    foreach ($entry in $values.GetEnumerator()) {
        $value = if ($entry.Value -is [SecureString]) {
            ConvertFrom-SecureString $entry.Value -AsPlainText
        } else { $entry.Value ?? '' }
        $parameters[$entry.Key] = @{ value = $value }
    }
    $parameterFile = Join-Path $workDirectory 'parameters.json'
    [IO.File]::WriteAllText($parameterFile, (@{ parameters = $parameters } | ConvertTo-Json -Depth 10),
        [Text.UTF8Encoding]::new($false))
    Invoke-Az account show --output none
    Invoke-Az group create --name $names.resourceGroup --location $Region --output none
    $mode = if ($WhatIf) { 'what-if' } else { 'create' }
    Invoke-Az deployment group $mode --resource-group $names.resourceGroup `
        --template-file (Join-Path $PSScriptRoot '..\infra\production.bicep') --parameters "@$parameterFile" --output none
    if (!$WhatIf) {
        Invoke-Az deployment group show --resource-group $names.resourceGroup --name production `
            --query properties.outputs --output json
    }
} finally {
    Remove-Item -LiteralPath $workDirectory -Recurse -Force
}

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('asp', 'microservices')]
    [string] $Template,
    [string] $Package,
    [string] $Workspace = (Join-Path ([IO.Path]::GetTempPath()) ("trellis-options-" + [Guid]::NewGuid().ToString('N'))),
    [switch] $MetadataOnly
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$configuration = Get-Content (Join-Path $repository "$Template\template\.template.config\template.json") -Raw | ConvertFrom-Json
$hostConfigurationPath = Join-Path $repository "$Template\template\.template.config\dotnetcli.host.json"

function Assert-True([bool] $Condition, [string] $Message) {
    if (!$Condition) { throw $Message }
}

$publishMatrixExpression = '${{ fromJSON(inputs.template == ''both'' && ''["asp", "microservices"]'' || format(''["{0}"]'', inputs.template)) }}'
foreach ($workflowName in @('publish-templates.yml', 'publish-templates-github.yml')) {
    $workflow = Get-Content (Join-Path $repository ".github\workflows\$workflowName") -Raw
    Assert-True ($workflow.Contains('options: [ both, asp, microservices ]')) "'$workflowName' must support publishing both templates in one run."
    Assert-True ($workflow.Contains('default: both')) "'$workflowName' must select both templates by default."
    Assert-True ($workflow.Contains($publishMatrixExpression)) "'$workflowName' must expand the selected templates into its publishing matrix."
    Assert-True ($workflow -match '(?m)^\s+fail-fast:\s+false\s*$') "'$workflowName' must not cancel the other template when one publishing job fails."
    Assert-True ($workflow.Contains('working-directory: ${{ matrix.template }}')) "'$workflowName' must run in the selected matrix template directory."
    Assert-True ($workflow.Replace($publishMatrixExpression, '') -notmatch 'inputs\.template') "'$workflowName' must use matrix.template in every per-template publishing step."
}

$defaults = @{
    apiVersioning = 'false'
    database = $(if ($Template -eq 'asp') { 'sqlite' } else { 'sqlserver' })
    auth = 'jwt'
    telemetryExporters = 'otlp'
    deployment = 'none'
    authorName = 'Your Name'
    rootNamespace = ''
    skipRestore = 'false'
}
$aliases = @{
    apiVersioning = 'api-versioning'
    database = 'database'
    auth = 'auth'
    telemetryExporters = 'telemetry-exporters'
    deployment = 'deployment'
    authorName = 'author-name'
    rootNamespace = 'root-namespace'
    skipRestore = 'skip-restore'
}
foreach ($symbol in $defaults.Keys) {
    $definition = $configuration.symbols.$symbol
    Assert-True ($null -ne $definition) "Missing shared option '$symbol' in $Template."
    Assert-True ($definition.type -eq 'parameter') "'$symbol' must be a public parameter."
    Assert-True ($definition.defaultValue -eq $defaults[$symbol]) "Wrong default for '$symbol' in $Template."
}
Assert-True ($configuration.symbols.UseApiVersioning.type -eq 'computed' -and
    $configuration.symbols.UseApiVersioning.value -eq 'apiVersioning') 'API versioning conditions must use the positive UseApiVersioning symbol.'
$exporterSymbols = @{
    UseOtlp = 'telemetryExporters == "otlp" || telemetryExporters == "both"'
    UseAzureMonitor = 'telemetryExporters == "azure-monitor" || telemetryExporters == "both"'
}
foreach ($symbol in $exporterSymbols.Keys) {
    $definition = $configuration.symbols.$symbol
    Assert-True ($definition.type -eq 'computed' -and $definition.value -eq $exporterSymbols[$symbol]) "Exporter conditions must use the positive '$symbol' symbol."
}
Assert-True ($null -eq $configuration.symbols.NoOtlp -and
    $null -eq $configuration.symbols.NoAzureMonitor) 'Negative exporter symbols must not remain in template configuration.'
[xml] $sourceTargets = Get-Content (Join-Path $repository 'Directory.Build.targets') -Raw
$sourceConstants = $sourceTargets.Project.PropertyGroup.DefineConstants.Split(';')
foreach ($symbol in $exporterSymbols.Keys) {
    Assert-True ($symbol -in $sourceConstants) "Source builds must retain coverage of the '$symbol' exporter."
}
Assert-True (Test-Path $hostConfigurationPath) 'Shared CLI aliases must be explicitly configured.'
$hostConfiguration = Get-Content $hostConfigurationPath -Raw | ConvertFrom-Json
foreach ($symbol in $aliases.Keys) {
    Assert-True ($hostConfiguration.symbolInfo.$symbol.longName -eq $aliases[$symbol]) "Wrong CLI spelling for '$symbol'."
}
$providers = @($configuration.symbols.database.choices | ForEach-Object choice)
Assert-True ('postgres' -in $providers -and 'sqlserver' -in $providers) 'Both server database providers must be supported.'
Assert-True (('sqlite' -in $providers) -eq ($Template -eq 'asp')) 'SQLite must be offered only by the ASP template.'
Assert-True ($configuration.symbols.database.isRequired -eq 'deployment == "azure"') 'Azure must require an explicit database choice.'
$expectedPackage = if ($Template -eq 'asp') { 'Trellis.Asp.Templates' } else { 'Trellis.Microservices.Templates' }
[xml] $packProject = Get-Content (Join-Path $repository "$Template\templatepack.csproj") -Raw
Assert-True ($expectedPackage -in @($packProject.Project.PropertyGroup.PackageId)) "Wrong package identity; expected $expectedPackage."
if ($MetadataOnly) { return }
Assert-True (![string]::IsNullOrWhiteSpace($Package)) '-Package is required for the packaged round-trip.'

$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path $Package).Path)
try {
    $paths = @($archive.Entries | ForEach-Object FullName)
    foreach ($path in $paths) {
        Assert-True ($path -notmatch '\\|//|^/|(^|/)\.\.?(/|$)') "Package entry '$path' must be a canonical relative archive path."
    }
    Assert-True ('content/Directory.Build.targets' -notin $paths) 'Source-only compiler constants must not be packed into generated apps.'
    $expectedDockerfiles = if ($Template -eq 'asp') {
        @('content/Dockerfile', 'content/.devcontainer/Dockerfile')
    } else {
        @('content/Gateway/src/Dockerfile', 'content/Members/Api/src/Dockerfile', 'content/Projects/Api/src/Dockerfile')
    }
    foreach ($path in $expectedDockerfiles) {
        Assert-True ($path -in $paths) "Dockerfile must be packed at '$path'."
    }
} finally { $archive.Dispose() }

New-Item -ItemType Directory -Path $Workspace -Force | Out-Null
$hive = Join-Path $Workspace 'hive'
function Invoke-Dotnet([string[]] $Arguments, [switch] $ExpectFailure) {
    & dotnet @Arguments | Out-Host
    $code = $LASTEXITCODE
    if ($ExpectFailure) {
        Assert-True ($code -ne 0) "Expected dotnet to reject: $($Arguments -join ' ')"
    } else {
        Assert-True ($code -eq 0) "dotnet exited $code`: $($Arguments -join ' ')"
    }
}
Invoke-Dotnet @('new', '--debug:custom-hive', $hive, 'install', (Resolve-Path $Package).Path)

$implicitWorkspace = Join-Path $Workspace 'implicit-output'
New-Item -ItemType Directory -Path $implicitWorkspace -Force | Out-Null
Push-Location $implicitWorkspace
try {
    Invoke-Dotnet @('new', $configuration.shortName, '--debug:custom-hive', $hive, '-n', 'ImplicitService', '--skip-restore')
    Assert-True (Test-Path 'ImplicitService\ImplicitService.slnx') 'Creation without -o must place the solution in the named child directory.'
    if ($Template -eq 'asp') {
        Assert-True (Test-Path 'ImplicitService\.devcontainer\Dockerfile') 'Implicit output must keep the dev-container Dockerfile inside the project.'
    }
} finally { Pop-Location }

function New-Profile([string] $Name, [string[]] $Options) {
    $output = Join-Path $Workspace $Name
    $arguments = @('new', $configuration.shortName, '--debug:custom-hive', $hive, '-n', $Name, '-o', $output, '--skip-restore') + $Options
    Invoke-Dotnet $arguments
    Assert-True (Test-Path (Join-Path $output "$Name.slnx")) 'Solution naming must continue to follow -n.'
    $context = Get-Content (Join-Path $output '.agentdocs\agent-context.json') -Raw | ConvertFrom-Json
    $entryPoints = @($context.Graph.EntryPoints)
    Assert-True ($entryPoints.Count -eq 1 -and $entryPoints[0].Path -eq "$Name.slnx") 'AgentDocs must reference the actual solution filename, not the C# namespace.'
    [xml] $solution = Get-Content (Join-Path $output "$Name.slnx") -Raw
    foreach ($item in $solution.SelectNodes('//File')) {
        Assert-True (Test-Path (Join-Path $output $item.Path)) "Solution item '$($item.Path)' must reference an emitted file."
    }
    if ($Template -eq 'asp') {
        $devcontainer = Join-Path $output '.devcontainer'
        Assert-True (Test-Path (Join-Path $devcontainer 'Dockerfile')) 'The dev-container Dockerfile must be adjacent to devcontainer.json, not in a repeated nested directory.'
        $definition = Get-Content (Join-Path $devcontainer 'devcontainer.json') -Raw | ConvertFrom-Json
        Assert-True ($definition.build.dockerfile -eq 'Dockerfile') 'The dev-container build must reference its adjacent Dockerfile.'
        Assert-True (@(Get-ChildItem $devcontainer -Recurse -Filter Dockerfile).Count -eq 1) 'The dev-container Dockerfile must be packed exactly once.'
    }
    return $output
}

function Assert-PaginationProfile([string] $Profile, [bool] $Versioned) {
    $paginationFile = if ($Template -eq 'asp') { 'TodosController.cs' } else { 'ProjectEndpoints.cs' }
    $paginationSources = @(Get-ChildItem $Profile -Recurse -Filter $paginationFile |
        Where-Object { $_.FullName -match '[\\/]src[\\/]' })
    $expectedCount = if ($Template -eq 'asp' -and $Versioned) { 2 } else { 1 }
    Assert-True ($paginationSources.Count -eq $expectedCount) 'Every selected paginated endpoint must be emitted.'
    foreach ($file in $paginationSources) {
        $source = Get-Content $file.FullName -Raw
        Assert-True ($source -match 'nextUrlBuilder:\s+(?:HttpContext|http)\.PageUrl\(') 'Versioned and unversioned pagination must use the common PageUrl builder.'
        Assert-True ($source -notmatch 'Url\.Link\(|GetUriByName\(') 'Pagination must not fall back to hand-written link generation.'
    }
    $registrations = if ($Template -eq 'asp') { @('Api\src\DependencyInjection.cs') } else {
        @('Members\Api\src\Program.cs', 'Projects\Api\src\Program.cs')
    }
    foreach ($path in $registrations) {
        $source = Get-Content (Join-Path $Profile $path) -Raw
        Assert-True (($source -match '\.UseAsp\(asp => asp\.UseVersionedPageUrls\(\)\)') -eq $Versioned) 'Only versioned hosts must configure the version-aware pagination policy.'
        if (!$Versioned) {
            Assert-True ($source -notmatch 'UseVersionedPageUrls') 'Unversioned hosts must omit the optional pagination policy entirely.'
        }
    }
}

function Assert-ExporterProfile([string] $Profile, [bool] $Otlp, [bool] $AzureMonitor) {
    $registrationPath = if ($Template -eq 'asp') { 'Api\src\DependencyInjection.cs' } else {
        'ServiceDefaults\src\Extensions.cs'
    }
    $source = Get-Content (Join-Path $Profile $registrationPath) -Raw
    Assert-True (($source -match '\.UseOtlpExporter\(') -eq $Otlp) 'Only selected profiles must register the OTLP exporter.'
    Assert-True (($source -match '\.UseAzureMonitor\(') -eq $AzureMonitor) 'Only selected profiles must register the Azure Monitor exporter.'
    Assert-True ($source -notmatch '#if\s*\(') 'Generation must resolve exporter conditions rather than emit compiler flags.'

    $projectPath = if ($Template -eq 'asp') { 'Api\src\Api.csproj' } else {
        'ServiceDefaults\src\ServiceDefaults.csproj'
    }
    [xml] $project = Get-Content (Join-Path $Profile $projectPath) -Raw
    [xml] $packages = Get-Content (Join-Path $Profile 'Directory.Packages.props') -Raw
    $references = @($project.Project.ItemGroup.PackageReference | ForEach-Object Include)
    $versions = @($packages.Project.ItemGroup.PackageVersion | ForEach-Object Include)
    foreach ($package in @{
        'OpenTelemetry.Exporter.OpenTelemetryProtocol' = $Otlp
        'Azure.Monitor.OpenTelemetry.AspNetCore' = $AzureMonitor
    }.GetEnumerator()) {
        Assert-True (($package.Key -in $references) -eq $package.Value) "Only selected profiles must reference '$($package.Key)'."
        Assert-True (($package.Key -in $versions) -eq $package.Value) "Only selected profiles must pin '$($package.Key)'."
    }
    $composePath = Join-Path $Profile 'compose.yaml'
    if (Test-Path $composePath) {
        $compose = Get-Content $composePath -Raw
        Assert-True (($compose -match 'OTEL_EXPORTER_OTLP_ENDPOINT:') -eq $Otlp) 'Container environments must match the selected OTLP exporter.'
        Assert-True (($compose -match 'APPLICATIONINSIGHTS_CONNECTION_STRING:') -eq $AzureMonitor) 'Container environments must match the selected Azure Monitor exporter.'
        Assert-True ($compose -notmatch '#if\s*\(') 'Generation must resolve exporter conditions in container composition.'
    }
    Assert-True (!(Test-Path (Join-Path $Profile 'Directory.Build.targets'))) 'Source-only compiler constants must not override the generated exporter profile.'
}

function Assert-NamespaceProfile([string] $Profile, [string] $Namespace) {
    $propertyPath = if ($Template -eq 'asp') { 'Directory.Build.props' } else { 'Projects\Domain\src\Projects.Domain.csproj' }
    [xml] $properties = Get-Content (Join-Path $Profile $propertyPath) -Raw
    $rootNamespace = if ($Template -eq 'asp') { $Namespace + '.$(MSBuildProjectName.Replace(" ", "_"))' } else { "$Namespace.Projects.Domain" }
    $assemblyName = if ($Template -eq 'asp') { $Namespace + '.$(MSBuildProjectName)' } else { "$Namespace.Projects.Domain" }
    Assert-True ($rootNamespace -in @($properties.Project.PropertyGroup.RootNamespace)) 'The root namespace must use the selected namespace without a leftover template suffix.'
    Assert-True ($assemblyName -in @($properties.Project.PropertyGroup.AssemblyName)) 'The assembly prefix must use the selected namespace independently of the solution filename.'
}

function Assert-DeploymentPreflightFailure([string] $Script, [hashtable] $Arguments, [string] $Expected) {
    function az { throw 'Deployment preflight unexpectedly reached Azure CLI.' }
    $failure = $null
    try { & $Script @Arguments }
    catch { $failure = $_.Exception.Message }
    Assert-True ($null -ne $failure -and $failure -match $Expected) "Expected deployment preflight '$Expected'; got '$failure'."
}

function Assert-PublishedKeyMounts([string] $Profile) {
    $source = Get-Content (Join-Path $Profile 'infra\production.bicep') -Raw
    $environment = [regex]::Match($source, '(?ms)^var publishedEnvironment = .*?^\}\]').Value
    $volume = [regex]::Match($source, '(?ms)^var publishedVolumeItems = .*?^\}\]').Value
    $environmentPath = [regex]::Match($environment, "value: '([^']+)'").Groups[1].Value
    $volumePath = [regex]::Match($volume, "path: '([^']+)'").Groups[1].Value
    Assert-True ($environmentPath.Contains('${key.key}') -and $volumePath.Contains('${key.key}')) 'Published-key paths must interpolate their aliases.'
    foreach ($alias in @('active', 'next', 'previous', 'published-active', 'abcdefghijklmnopqrst')) {
        $configuredPath = $environmentPath.Replace('${key.key}', $alias)
        $mountedPath = '/keys/' + $volumePath.Replace('${key.key}', $alias)
        Assert-True ($configuredPath -eq $mountedPath) "Published key '$alias' must reference its actual mounted file."
        Assert-True ($mountedPath -eq "/keys/published-$alias.pem") "Published key '$alias' must use a separate filename namespace from the private signer."
        Assert-True ($mountedPath -ne '/keys/active.pem') "Published key '$alias' must not overwrite the active private signer."
    }
}

$defaultName = if ($Template -eq 'microservices') { 'ProjectTracker' } else { 'DefaultService' }
$default = New-Profile $defaultName @()
Assert-NamespaceProfile $default $defaultName
Assert-PaginationProfile $default $false
Assert-ExporterProfile $default $true $false
$profile = Get-Content (Join-Path $default '.trellis-template.json') -Raw | ConvertFrom-Json
Assert-True ($profile.template -eq $Template -and [bool]::Parse($profile.apiVersioning) -eq $false) 'The emitted profile must record the selected defaults.'
$productionSources = Get-ChildItem $default -Recurse -Filter '*.cs' |
    Where-Object { $_.FullName -match '[\\/]src[\\/]' } |
    ForEach-Object { Get-Content $_.FullName -Raw }
$projects = Get-ChildItem $default -Recurse -Filter '*.csproj' | ForEach-Object { Get-Content $_.FullName -Raw }
Assert-True (($productionSources -join "`n") -notmatch 'using Asp\.Versioning|using Trellis\.Asp\.ApiVersioning|AddApiVersioning\(|\.WithVersionedRoute\(|\.AddApiVersion\(') 'Default production sources must be genuinely unversioned.'
Assert-True (($projects -join "`n") -notmatch 'Include="(?:Asp\.Versioning[^"]*|Trellis\.Asp\.ApiVersioning|Trellis\.ServiceLevelIndicators\.Asp\.ApiVersioning|Azure\.Monitor\.OpenTelemetry[^"]*)"') 'Default projects must omit versioning and Azure Monitor dependencies.'
Assert-True (!(Test-Path (Join-Path $default 'infra')) -and !(Test-Path (Join-Path $default 'deploy'))) 'No deployment assets may be emitted by default.'
if ($Template -eq 'asp') {
    Assert-True (Test-Path (Join-Path $default 'Api\src\Controllers\TodosController.cs')) 'Unversioned controllers need an undated layout.'
    Assert-True (!(Test-Path (Join-Path $default 'Api\src\2026-03-26')) -and !(Test-Path (Join-Path $default 'Api\src\2026-12-01'))) 'Default output must not contain dated controller folders.'
}

$custom = New-Profile 'CustomService' @('--author-name', 'Example & Partners <Engineering>', '--root-namespace', 'Example.Billing', '--api-versioning', '--database', 'postgres', '--auth', 'entra', '--telemetry-exporters', 'both', '--deployment', 'container')
Assert-NamespaceProfile $custom 'Example.Billing'
Assert-PaginationProfile $custom $true
Assert-ExporterProfile $custom $true $true
[xml] $properties = Get-Content (Join-Path $custom 'Directory.Build.props') -Raw
Assert-True ('Example & Partners <Engineering>' -in @($properties.Project.PropertyGroup.Authors)) 'Author replacement must preserve XML-special characters in the generated build properties.'
$customSources = Get-ChildItem $custom -Recurse -Filter '*.cs' |
    Where-Object { $_.FullName -match '[\\/]src[\\/]' } |
    ForEach-Object { Get-Content $_.FullName -Raw }
Assert-True (($customSources -join "`n") -match 'namespace Example\.Billing') 'The custom root namespace must reach the generated source.'
Assert-True (($customSources -join "`n") -match 'AddApiVersioning') 'Versioning must be wired when selected.'
Assert-True (($customSources -join "`n") -match 'AddNpgsqlDbContext|UseNpgsql') 'PostgreSQL must be wired when selected.'
Assert-True (($customSources -join "`n") -match 'UseEntraActorProvider|AddEntraActorProvider') 'Entra must select its production actor provider.'
Assert-True (Test-Path (Join-Path $custom 'compose.yaml')) 'Container output needs a runnable composition.'
$dockerfiles = @(Get-ChildItem $custom -Recurse -Filter Dockerfile |
    Where-Object { $_.FullName -notmatch '[\\/]\.devcontainer[\\/]' })
Assert-True ($dockerfiles.Count -eq $(if ($Template -eq 'asp') { 1 } else { 3 })) 'All selected container images must be packed at their proper output locations.'
$expectedDockerfiles = if ($Template -eq 'asp') { @('Dockerfile') } else {
    @('Gateway\src\Dockerfile', 'Members\Api\src\Dockerfile', 'Projects\Api\src\Dockerfile')
}
foreach ($path in $expectedDockerfiles) {
    Assert-True (Test-Path (Join-Path $custom $path)) "Container Dockerfile must be emitted at '$path', not a repeated nested directory."
}
Assert-True (!(Test-Path (Join-Path $default 'Api\src\obj')) -and
    !(Test-Path (Join-Path $default 'Gateway\src\obj'))) '--skip-restore must not run a post-creation restore.'
Assert-True (!(($customSources -join "`n") -match '#pragma warning disable IDE0047')) 'Source-only preprocessor suppression must not leak into generated projects.'

$otlpContainer = New-Profile 'OtlpContainerService' @('--deployment', 'container', '--telemetry-exporters', 'otlp')
Assert-ExporterProfile $otlpContainer $true $false
$azureMonitorContainer = New-Profile 'AzureMonitorContainerService' @('--deployment', 'container', '--telemetry-exporters', 'azure-monitor')
Assert-ExporterProfile $azureMonitorContainer $false $true

$azure = New-Profile 'AzureService' @('--deployment', 'azure', '--database', 'sqlserver', '--telemetry-exporters', 'azure-monitor')
Assert-PaginationProfile $azure $false
Assert-ExporterProfile $azure $false $true
Assert-True (Test-Path (Join-Path $azure 'infra')) 'Azure infrastructure must be emitted when selected.'
$azurePostgres = New-Profile 'AzurePostgresService' @('--deployment', 'azure', '--database', 'postgres', '--auth', 'entra', '--telemetry-exporters', 'both')
Assert-PaginationProfile $azurePostgres $false
Assert-ExporterProfile $azurePostgres $true $true
if ($Template -eq 'asp') {
    Assert-DeploymentPreflightFailure (Join-Path $azure 'deploy\deploy.ps1') @{} 'HTTPS -AuthenticationAuthority'
    Assert-DeploymentPreflightFailure (Join-Path $azurePostgres 'deploy\deploy.ps1') @{} 'Entra GUIDs'
} else {
    Assert-PublishedKeyMounts $azure
    Assert-PublishedKeyMounts $azurePostgres
    $arguments = @{
        DatabaseAdministratorLogin = 'preflight-only'
        DatabaseAdministratorPassword = ConvertTo-SecureString 'preflight-only' -AsPlainText -Force
    }
    Assert-DeploymentPreflightFailure (Join-Path $azure 'deploy\deploy.ps1') $arguments 'all three image references'
}
Invoke-Dotnet @('new', $configuration.shortName, '--debug:custom-hive', $hive, '-n', 'MissingDatabase', '-o', (Join-Path $Workspace 'MissingDatabase'), '--deployment', 'azure', '--skip-restore') -ExpectFailure
if ($Template -eq 'asp') {
    $invalid = New-Profile 'InvalidAzure' @('--deployment', 'azure', '--database', 'sqlite')
    Push-Location $invalid
    try { Invoke-Dotnet @('restore', 'InvalidAzure.slnx') -ExpectFailure }
    finally { Pop-Location }
    Invoke-Dotnet @('new', $configuration.shortName, '--debug:custom-hive', $hive, '-n', 'InvalidAzureAutoRestore',
        '-o', (Join-Path $Workspace 'InvalidAzureAutoRestore'), '--deployment', 'azure', '--database', 'sqlite') -ExpectFailure
}
$namespace = New-Profile '9-Billing' @()
Assert-NamespaceProfile $namespace '_9_Billing'
Push-Location $namespace
try {
    Invoke-Dotnet @('build', '9-Billing.slnx', '-c', 'Release', '--verbosity', 'quiet')
    $namespaceSource = Get-ChildItem -Recurse -Filter '*.cs' |
        Where-Object { $_.FullName -match '[\\/]src[\\/]' } |
        ForEach-Object { Get-Content $_.FullName -Raw }
    Assert-True (($namespaceSource -join "`n") -match 'namespace _9_Billing') 'Default namespace derivation must sanitize leading digits and hyphens.'
} finally { Pop-Location }
foreach ($profile in @($default, $custom, $azure, $azurePostgres)) {
    Push-Location $profile
    try {
        $solutions = @(Get-ChildItem -Filter '*.slnx')
        Assert-True ($solutions.Count -eq 1) 'Each profile must produce one solution.'
        $solution = $solutions[0].Name
        Invoke-Dotnet @('build', $solution, '-c', 'Release', '--verbosity', 'quiet')
        Invoke-Dotnet @('test', '--solution', $solution, '-c', 'Release', '--no-build', '--', '--filter-not-trait', 'Category=Integration')
        & git init --quiet
        Assert-True ($LASTEXITCODE -eq 0) 'Generated guidance must be checked in its own Git root.'
        Invoke-Dotnet @('tool', 'restore')
        Invoke-Dotnet @('tool', 'run', 'agentdocs', 'sync', '--strict', '--strict-references')
        Invoke-Dotnet @('tool', 'run', 'agentdocs', 'check', '--strict', '--strict-references')
        Invoke-Dotnet @('run', '--project', (Join-Path $repository 'shared\contract-tests\runner.csproj'), '-c', 'Release', '--',
            (Join-Path $repository 'shared\capability-parity-manifest.yaml'), $Template, $profile)
        if (Test-Path 'deploy\names\names.csproj') {
            Invoke-Dotnet @('build', 'deploy\names\names.csproj', '-c', 'Release', '--verbosity', 'quiet')
        }
    } finally { Pop-Location }
}

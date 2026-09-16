param(
    [Parameter(Mandatory)][ValidateSet('single', 'folder')][string]$Package,
    [Parameter(Mandatory)][string]$SourceDirectory
)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or
    $env:GITHUB_REPOSITORY -ne 'arussin/codex-usage') {
    throw 'This build requires a disposable GitHub-hosted runner for arussin/codex-usage.'
}
$sourceRoot = (Resolve-Path -LiteralPath $SourceDirectory).Path
$expectedRevision = if ($Package -eq 'single') { '14f8e8f22605e35464cac6d2cf614cbbccc57d68' } else { 'ade5f362abcab194e90678a15bd14f56c9bd29cf' }
$revision = git -C $sourceRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $revision -ne $expectedRevision) { throw 'Unexpected source revision' }
if (git -C $sourceRoot status --porcelain) { throw 'Source checkout is not clean' }
$buildRoot = Join-Path $env:RUNNER_TEMP 'startup-lab-build'
if (Test-Path -LiteralPath $buildRoot) { throw 'Build directory must be new' }
$copyRoot = Join-Path $buildRoot 'source'
$publish = Join-Path $env:RUNNER_TEMP 'release with spaces'
if (Test-Path -LiteralPath $publish) { throw 'Publish directory must be new' }
New-Item -ItemType Directory -Path $copyRoot -Force | Out-Null
$sourceHashes = @()
Get-ChildItem -LiteralPath $sourceRoot -File | Where-Object { $_.Name -match '\.(cs|csproj)$|^LICENSE$' } | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $copyRoot $_.Name)
    $sourceHashes += @{ name = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant() }
}
@{ sdk = @{ version = '10.0.401'; rollForward = 'disable' } } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $buildRoot 'global.json')
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$project = Join-Path $copyRoot 'CodexUsageTray.csproj'
$properties = @(
    '-p:ImportDirectoryBuildProps=false', '-p:ImportDirectoryBuildTargets=false',
    "-p:SourceRevisionId=$revision", "-p:PathMap=$copyRoot=/_/codex-usage",
    '-p:RuntimeFrameworkVersion=10.0.12'
)
if ($Package -eq 'single') {
    $properties += @('-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true', '-p:PublishTrimmed=false', '-p:DebugType=embedded')
}
Push-Location $buildRoot
try {
    $sdk = dotnet --version
    if ($LASTEXITCODE -ne 0 -or $sdk -ne '10.0.401') { throw 'Original SDK is not selected' }
    dotnet restore $project --runtime win-x64 -p:SelfContained=true @properties
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
    dotnet build $project --configuration Release --runtime win-x64 --no-restore -p:SelfContained=true @properties
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    dotnet publish $project --configuration Release --runtime win-x64 --self-contained true --no-restore --no-build --output $publish @properties
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    $builtExe = Join-Path $publish 'CodexUsageTray.exe'
    $hash = (Get-FileHash -LiteralPath $builtExe).Hash.ToLowerInvariant()
    $match = if ($Package -eq 'single') { $hash -eq '5b46e1cf80690e57699c7dfc3a2277103a5fc855a90ed37e004016956e26740a' } else { $null }
    @{
        sourceCommit = $revision
        sourceFiles = $sourceHashes
        sdkVersion = $sdk
        runtimeVersion = '10.0.12'
        target = 'win-x64'
        package = $Package
        exeSha256 = $hash
        exactSingleExeDraftHashMatch = $match
        result = 'build-passed'
        applicationSourceModified = $false
        source = 'Rebuilt from public source; draft assets were not read or published.'
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $env:RUNNER_TEMP 'startup-lab-build.json')
} finally { Pop-Location }

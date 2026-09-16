param(
    [Parameter(Mandatory = $true)][string]$SourceDirectory,
    [Parameter(Mandatory = $true)][string]$WorkDirectory,
    [Parameter(Mandatory = $true)][string]$Revision,
    [string]$Dotnet = 'dotnet',
    [ValidateSet('SingleFile', 'Folder')][string]$PackageLayout = 'SingleFile'
)
$ErrorActionPreference = 'Stop'
$sourceRoot = (Resolve-Path -LiteralPath $SourceDirectory).Path
$workRoot = [IO.Path]::GetFullPath($WorkDirectory)
if ($Revision -notmatch '^[0-9a-f]{40}$') { throw 'Expected a full source commit SHA.' }
if ($workRoot -eq $sourceRoot -or $workRoot.StartsWith($sourceRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Build output must be outside the source checkout.'
}
if ((Test-Path -LiteralPath $workRoot) -and @(Get-ChildItem -LiteralPath $workRoot -Force).Count -ne 0) {
    throw 'Build workspace must be empty.'
}
$null = New-Item -ItemType Directory -Path $workRoot -Force
$copyRoot = Join-Path $workRoot 'source'
$null = New-Item -ItemType Directory -Path $copyRoot
Get-ChildItem -LiteralPath $sourceRoot -File | Where-Object { $_.Name -match '\.(cs|csproj)$|^LICENSE$' } | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $copyRoot $_.Name)
}
$project = Join-Path $copyRoot 'CodexUsageTray.csproj'
if (!(Test-Path -LiteralPath $project)) { throw 'Project file missing.' }
$env:DOTNET_CLI_HOME = Join-Path $workRoot 'dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$publish = Join-Path $workRoot 'publish'
# A fixed source path prevents personal workspace paths in debugging metadata.
$properties = @('-p:ImportDirectoryBuildProps=false', '-p:ImportDirectoryBuildTargets=false', "-p:SourceRevisionId=$Revision", "-p:PathMap=$copyRoot=/_/codex-usage")
$singleFile = $PackageLayout -eq 'SingleFile'
# Keep the full runtime; WinForms and reflection-dependent code are not trimmed.
# Native runtime libraries extract automatically to the per-user .NET cache.
$properties += @(
    "-p:PublishSingleFile=$($singleFile.ToString().ToLowerInvariant())",
    "-p:IncludeNativeLibrariesForSelfExtract=$($singleFile.ToString().ToLowerInvariant())",
    "-p:EnableCompressionInSingleFile=$($singleFile.ToString().ToLowerInvariant())",
    '-p:PublishTrimmed=false',
    '-p:DebugType=embedded'
)
& $Dotnet restore $project --runtime win-x64 -p:SelfContained=true @properties
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
& $Dotnet build $project --configuration Release --runtime win-x64 --no-restore -p:SelfContained=true @properties
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
& $Dotnet publish $project --configuration Release --runtime win-x64 --self-contained true --no-restore --no-build --output $publish @properties
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
if ($singleFile) {
    $publishedFiles = @(Get-ChildItem -LiteralPath $publish -File -Recurse)
    if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].Name -ne 'CodexUsageTray.exe') {
        throw 'Single-file output contains unexpected loose files.'
    }
}
Copy-Item -LiteralPath (Join-Path $copyRoot 'LICENSE') -Destination (Join-Path $publish 'LICENSE')
# This is the existing test-only mode. It uses temporary fixtures/registry state,
# may briefly show a test popup, and performs one authenticated quota read.
# It neither starts a normal tray nor writes the live phone export.
$test = Start-Process -FilePath (Join-Path $publish 'CodexUsageTray.exe') -ArgumentList '--self-test' -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $workRoot 'self-test.stdout.txt') -RedirectStandardError (Join-Path $workRoot 'self-test.stderr.txt')
if (!$test.WaitForExit(180000)) {
    Stop-Process -Id $test.Id -ErrorAction SilentlyContinue
    throw 'Only the newly started self-test process was stopped after its timeout.'
}
$test.WaitForExit()
if ($test.ExitCode -ne 0) { throw 'Self-tests failed; inspect the local build logs.' }
& $Dotnet format $project --verify-no-changes --no-restore --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw 'Formatting check failed.' }
@{ sourceCommit = $Revision; configuration = 'Release'; runtime = 'win-x64'; selfContained = $true; packageLayout = $PackageLayout; compressed = $singleFile; trimmed = $false; selfTests = 'passed'; format = 'passed'; normalTrayStarted = $false; interactiveAcceptance = 'pending' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $workRoot 'build-result.json') -Encoding UTF8
Write-Output 'Build, self-tests and formatting passed. Nothing was installed.'

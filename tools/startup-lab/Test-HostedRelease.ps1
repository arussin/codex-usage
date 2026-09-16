param([Parameter(Mandatory)][ValidateSet('single', 'folder')][string]$Package)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# This harness intentionally runs the real tray and writes synthetic preferences.
# Refuse personal PCs and self-hosted runners. Never invoke it on a live installation.
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or
    $env:GITHUB_REPOSITORY -ne 'arussin/codex-usage') {
    throw 'This test requires a disposable GitHub-hosted runner for arussin/codex-usage.'
}
if ($env:GH_TOKEN -or $env:GITHUB_TOKEN -or $env:DOTNET_STARTUP_HOOKS) {
    throw 'Do not pass credentials or startup hooks to the application test.'
}

$report = [ordered]@{
    schemaVersion = 1
    package = $Package
    scope = 'Rebuild of published source, synthetic CLI, disposable GitHub-hosted Windows VM. Not a Windows sign-in test.'
    result = 'incomplete'
    stage = 'preflight'
    checks = [ordered]@{}
    limitations = @(
        'No Windows sign-out/sign-in or physical Surface test is performed.',
        'The runner uses administrator privileges with UAC disabled; it does not reproduce the Surface security configuration.',
        'Windows 11 Arm runs the x64 candidate through emulation; Windows Server uses native x64.',
        'No visual layout, tray clicking, phone or real account test is claimed.',
        'Test processes are terminated by the harness, not by the tray Exit menu.'
    )
}
$reportFile = Join-Path $env:RUNNER_TEMP 'startup-lab-report.json'
$children = [Collections.Generic.List[Diagnostics.Process]]::new()
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$beforeRun = $null
$runCaptured = $false

function Assert-Lab([bool]$Condition, [string]$Name) {
    $report.checks[$Name] = $Condition
    if (-not $Condition) { throw "Lab assertion failed: $Name" }
}
function Get-RunValue {
    $key = Get-Item -LiteralPath $runKey -ErrorAction SilentlyContinue
    if ($null -eq $key) { return $null }
    return $key.GetValue('CodexUsageTray', $null)
}
function Start-LabProcess([string[]]$Arguments = @()) {
    $info = [Diagnostics.ProcessStartInfo]::new($script:candidate)
    $info.WorkingDirectory = Join-Path $env:WINDIR 'System32'
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.Environment['PATH'] = $script:fixtureRoot + ';' + (Join-Path $env:WINDIR 'System32')
    $info.Environment['CODEX_LAB_MODE'] = $script:modeFile
    $info.Environment['CODEX_LAB_CALLS'] = $script:callsFile
    $info.Environment['CODEX_HOME'] = Join-Path $script:fixtureRoot 'empty-codex-home'
    foreach ($key in @('GH_TOKEN', 'GITHUB_TOKEN', 'DOTNET_STARTUP_HOOKS', 'DOTNET_BUNDLE_EXTRACT_BASE_DIR')) {
        [void]$info.Environment.Remove($key)
    }
    foreach ($arg in $Arguments) { $info.ArgumentList.Add($arg) }
    $child = [Diagnostics.Process]::Start($info)
    $children.Add($child)
    return $child
}
function Stop-LabProcess([Diagnostics.Process]$Child) {
    if (-not $Child.HasExited) {
        $Child.Kill($true)
        [void]$Child.WaitForExit(10000)
    }
}
function Wait-Lab([scriptblock]$Condition, [int]$Seconds, [Diagnostics.Process]$Child) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        if ($Child.HasExited) { throw 'The application exited before the expected observation.' }
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 500
    } while ($timer.Elapsed.TotalSeconds -lt $Seconds)
    throw 'The expected observation did not arrive before the bounded timeout.'
}

try {
    Assert-Lab (-not @(Get-Process -Name CodexUsageTray -ErrorAction SilentlyContinue).Count) 'freshMachineNoTray'
    $settingsRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'CodexUsageTray'
    $defaultOutput = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'CodexUsagePhone\usage.json'
    $desktopCli = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'OpenAI\Codex\bin'
    Assert-Lab (-not (Test-Path -LiteralPath $settingsRoot)) 'freshMachineNoPreferences'
    Assert-Lab (-not (Test-Path -LiteralPath $defaultOutput)) 'freshMachineNoExport'
    Assert-Lab (-not (Test-Path -LiteralPath $desktopCli)) 'noInstalledDesktopCli'
    $beforeRun = Get-RunValue
    $runCaptured = $true
    Assert-Lab ($null -eq $beforeRun) 'noProductionStartupRegistration'
    $os = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    $report.environment = [ordered]@{
        edition = $os.EditionID
        build = $os.CurrentBuildNumber
        revision = $os.UBR
        osArchitecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        harnessArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        userInteractive = [Environment]::UserInteractive
        explorerProcessCount = @(Get-Process explorer -ErrorAction SilentlyContinue).Count
        sameSessionExplorerCount = @(Get-Process explorer -ErrorAction SilentlyContinue | Where-Object SessionId -eq (Get-Process -Id $PID).SessionId).Count
        candidateArchitecture = 'x64'
    }
    $report.stage = 'verify-rebuild'
    $metadata = Get-Content -LiteralPath (Join-Path $env:RUNNER_TEMP 'startup-lab-build.json') -Raw | ConvertFrom-Json
    $packageRoot = Join-Path $env:RUNNER_TEMP 'release with spaces'
    $script:candidate = Join-Path $packageRoot 'CodexUsageTray.exe'
    $report.exeSha256 = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-Lab ($metadata.package -eq $Package -and $metadata.exeSha256 -eq $report.exeSha256) 'rebuiltExecutableIdentity'
    $report.sourceCommit = $metadata.sourceCommit
    $report.exactSingleExeDraftHashMatch = $metadata.exactSingleExeDraftHashMatch
    $report.signatureStatus = (Get-AuthenticodeSignature -LiteralPath $candidate).Status.ToString()
    $report.quotedStartupCommandLength = ('"' + $candidate + '"').Length

    $script:fixtureRoot = Join-Path $env:RUNNER_TEMP 'synthetic CLI'
    New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Fixture-Codex.ps1') -Destination $fixtureRoot
    $fixtureScript = Join-Path $fixtureRoot 'Fixture-Codex.ps1'
    $fixtureCommand = '@echo off' + "`r`n" + '"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $fixtureScript + '"' + "`r`n"
    [IO.File]::WriteAllText((Join-Path $fixtureRoot 'codex.cmd'), $fixtureCommand)
    $script:modeFile = Join-Path $fixtureRoot 'mode.txt'
    $script:callsFile = Join-Path $fixtureRoot 'calls.txt'
    [IO.File]::WriteAllText($modeFile, 'success')
    [IO.File]::WriteAllText($callsFile, '')

    $report.stage = 'existing-self-tests-with-synthetic-cli'
    $selfTest = Start-LabProcess @('--self-test')
    $outTask = $selfTest.StandardOutput.ReadToEndAsync()
    $errTask = $selfTest.StandardError.ReadToEndAsync()
    Assert-Lab ($selfTest.WaitForExit(60000)) 'selfTestsFinished'
    Assert-Lab ($selfTest.ExitCode -eq 0 -and $outTask.Result.Contains('Self-tests passed.')) 'existingSelfTestsWithFixturePassed'
    # Raw output is intentionally not published; the fixture is not a live account test.
    [void]$errTask.Result

    $report.stage = 'normal-entry-export-default-off'
    [IO.File]::WriteAllText($callsFile, '')
    $normal = Start-LabProcess
    Wait-Lab { [IO.File]::ReadAllText($callsFile).Contains('success') } 30 $normal
    Start-Sleep -Seconds 2
    Assert-Lab (-not $normal.HasExited) 'normalEntrySurvivesInitialRefresh'
    Assert-Lab (-not (Test-Path -LiteralPath $defaultOutput)) 'exportDefaultOff'
    Stop-LabProcess $normal

    $report.stage = 'normal-entry-export-opted-in'
    New-Item -ItemType Directory -Path $settingsRoot -Force | Out-Null
    $outputFile = Join-Path $fixtureRoot 'usage.json'
    @{ Enabled = $true; OutputPath = $outputFile } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $settingsRoot 'json-export-settings.json')
    [IO.File]::WriteAllText($callsFile, '')
    $normal = Start-LabProcess
    $initialLaunch = [Diagnostics.Stopwatch]::StartNew()
    Wait-Lab { Test-Path -LiteralPath $outputFile } 30 $normal
    $snapshot = Get-Content -LiteralPath $outputFile -Raw | ConvertFrom-Json
    Assert-Lab ($snapshot.fiveHour.remaining -eq 75 -and $snapshot.weekly.remaining -eq 60) 'normalEntryWritesSyntheticSnapshot'
    Assert-Lab ($snapshot.fiveHour.available -and $snapshot.weekly.available) 'snapshotAvailability'
    $beforeHash = (Get-FileHash -LiteralPath $outputFile).Hash
    $beforeWriteTicks = (Get-Item -LiteralPath $outputFile).LastWriteTimeUtc.Ticks
    $duplicate = Start-LabProcess
    Assert-Lab ($duplicate.WaitForExit(10000)) 'duplicateProcessExits'
    Assert-Lab ($duplicate.ExitCode -eq 0 -and -not $normal.HasExited) 'duplicateLeavesOriginalRunning'

    $report.stage = 'wait-for-real-five-minute-timer'
    [IO.File]::WriteAllText($modeFile, 'error')
    Wait-Lab { [IO.File]::ReadAllText($callsFile).Contains('error') } 325 $normal
    $report.secondsFromLaunchToFailedTimerRefresh = [Math]::Round($initialLaunch.Elapsed.TotalSeconds, 1)
    Start-Sleep -Seconds 2
    Assert-Lab (-not $normal.HasExited) 'normalEntrySurvivesTimerRefreshFailure'
    Assert-Lab ((Get-FileHash -LiteralPath $outputFile).Hash -eq $beforeHash) 'failurePreservesSnapshotBytesAndReadingTime'
    Assert-Lab ((Get-Item -LiteralPath $outputFile).LastWriteTimeUtc.Ticks -eq $beforeWriteTicks) 'failurePreservesFileTimestamp'
    Assert-Lab (-not (Test-Path -LiteralPath ($outputFile + '.tmp'))) 'noTemporaryExportLeftAfterFailure'
    Stop-LabProcess $normal

    $report.stage = 'recovery-after-relaunch'
    [IO.File]::WriteAllText($modeFile, 'success')
    [IO.File]::WriteAllText($callsFile, '')
    $normal = Start-LabProcess
    Wait-Lab { (Get-FileHash -LiteralPath $outputFile).Hash -ne $beforeHash } 30 $normal
    $recovered = Get-Content -LiteralPath $outputFile -Raw | ConvertFrom-Json
    Assert-Lab ($recovered.weekly.remaining -eq 60 -and $recovered.refreshedAt -ne $snapshot.refreshedAt) 'successfulRelaunchPublishesFreshSyntheticReading'
    Stop-LabProcess $normal
    $report.result = 'passed'
    $report.stage = 'complete'
} catch {
    $report.result = 'failed'
    $report.errorType = $_.Exception.GetType().Name
    Write-Host ('Lab failed during stage: ' + $report.stage)
} finally {
    foreach ($child in $children) {
        try { Stop-LabProcess $child } catch { $report.result = 'failed'; $report.cleanupErrorType = $_.Exception.GetType().Name }
    }
    if ($runCaptured) {
        $report.productionStartupRegistrationUnchanged = ((Get-RunValue) -eq $beforeRun)
        if (-not $report.productionStartupRegistrationUnchanged) { $report.result = 'failed' }
    }
    $report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $reportFile
    Write-Host ('Result: ' + $report.result)
    if ($env:GITHUB_STEP_SUMMARY) {
        @(
            "## $Package release: $($report.result)",
            '',
            'Rebuilt published source with synthetic CLI input on a disposable hosted runner.',
            "Stage: $($report.stage).",
            '',
            '**This is not a Windows sign-in test and does not clear the Surface startup failure.**',
            'No personal data, sign-in credentials, application binary or raw event trace is in the uploaded report.'
        ) | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY
    }
}
if ($report.result -ne 'passed') { exit 1 }

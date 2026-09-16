param([Parameter(Mandatory)][ValidateSet('single', 'folder')][string]$Package)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or
    $env:GITHUB_REPOSITORY -ne 'arussin/codex-usage') {
    throw 'This experiment requires a disposable GitHub-hosted runner for arussin/codex-usage.'
}
# This is an experiment with the documented RemoteApp startup dispatcher, not
# a Windows sign-in simulation. A control entry distinguishes a dispatcher
# no-op from evidence about the candidate. Never use this on a personal PC.
$report = [ordered]@{
    schemaVersion = 1
    package = $Package
    scope = 'Manual Windows startup-dispatch experiment in a disposable VM; not a Windows sign-in.'
    dispatcher = 'runonce.exe /AlternateShellStartup'
    documentation = 'https://learn.microsoft.com/en-us/troubleshoot/windows-server/remote/application-not-start-in-remoteapp-session'
    result = 'incomplete'
    controlObserved = $false
    candidateObserved = $false
    eventCreationCount = 0
    limitations = @(
        'The documented RemoteApp command is exercised in an existing ordinary interactive runner session.',
        'Windows 11 behavior here is experimental; the referenced Microsoft article applies to Windows Server.',
        'No sign-out, new sign-in, reboot, startup-delay override or security-policy change is performed.',
        'A missing control makes this dispatcher experiment inconclusive, not an application failure.',
        'The runner is privileged and its security environment differs from the Surface.'
    )
}
$runPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$controlName = 'CodexUsageLab.Control'
$candidateName = 'CodexUsageTray'
$candidate = Join-Path $env:RUNNER_TEMP 'release with spaces\CodexUsageTray.exe'
$marker = Join-Path $env:RUNNER_TEMP 'startup-lab-control.txt'
$eventName = 'CodexUsageLab.ProcessCreation'
$registered = $false
$subscription = $null
$dispatcher = $null
$observed = $null
try {
    $metadata = Get-Content -LiteralPath (Join-Path $env:RUNNER_TEMP 'startup-lab-build.json') -Raw | ConvertFrom-Json
    $hash = (Get-FileHash -LiteralPath $candidate).Hash.ToLowerInvariant()
    if ($hash -ne $metadata.exeSha256 -or $metadata.package -ne $Package) { throw 'Candidate identity mismatch' }
    $report.exeSha256 = $hash
    $report.exactSingleExeDraftHashMatch = $metadata.exactSingleExeDraftHashMatch
    if (@(Get-Process -Name CodexUsageTray -ErrorAction SilentlyContinue).Count) { throw 'A tray is already running' }
    if (Test-Path -LiteralPath $marker) { throw 'Control marker must be new' }
    $key = Get-Item -LiteralPath $runPath -ErrorAction SilentlyContinue
    if ($key -and ($key.GetValueNames() -contains $candidateName -or $key.GetValueNames() -contains $controlName)) {
        throw 'Experiment refuses to replace existing startup entries'
    }
    $report.preexistingCurrentUserRunEntryCount = if ($key) { $key.GetValueNames().Count } else { 0 }
    [void](Register-CimIndicationEvent -Query "SELECT * FROM Win32_ProcessStartTrace WHERE ProcessName='CodexUsageTray.exe'" -SourceIdentifier $eventName)
    $subscription = $true
    if (-not (Test-Path -LiteralPath $runPath)) { New-Item -Path $runPath | Out-Null }
    $candidateCommand = '"' + $candidate + '"'
    $controlCommand = '"' + (Join-Path $env:WINDIR 'System32\cmd.exe') + '" /d /c echo control > "' + $marker + '"'
    if ($candidateCommand.Length -gt 260 -or $controlCommand.Length -gt 260) { throw 'Startup command exceeds documented limit' }
    $registered = $true
    New-ItemProperty -LiteralPath $runPath -Name $candidateName -Value $candidateCommand -PropertyType String | Out-Null
    New-ItemProperty -LiteralPath $runPath -Name $controlName -Value $controlCommand -PropertyType String | Out-Null
    $report.candidateRegistrationReadbackMatches = (Get-Item -LiteralPath $runPath).GetValue($candidateName) -eq $candidateCommand
    $report.quotedCommandLength = $candidateCommand.Length
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $env:WINDIR 'System32\runonce.exe'))
    $start.ArgumentList.Add('/AlternateShellStartup')
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WorkingDirectory = Join-Path $env:WINDIR 'System32'
    $start.Environment['PATH'] = (Join-Path $env:RUNNER_TEMP 'synthetic CLI') + ';' + (Join-Path $env:WINDIR 'System32')
    $start.Environment['CODEX_LAB_MODE'] = Join-Path $env:RUNNER_TEMP 'synthetic CLI\mode.txt'
    $start.Environment['CODEX_LAB_CALLS'] = Join-Path $env:RUNNER_TEMP 'synthetic CLI\calls.txt'
    foreach ($name in @('GH_TOKEN', 'GITHUB_TOKEN', 'DOTNET_STARTUP_HOOKS')) { [void]$start.Environment.Remove($name) }
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $dispatcher = [Diagnostics.Process]::Start($start)
    do {
        $report.controlObserved = Test-Path -LiteralPath $marker
        $matches = @(Get-Process -Name CodexUsageTray -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $candidate })
        if ($matches.Count -gt 1) { throw 'Unexpected duplicate candidate processes' }
        if ($matches.Count -eq 1) {
            $observed = $matches[0]
            if (-not $report.candidateObserved) { $report.candidateFirstObservedSeconds = [Math]::Round($timer.Elapsed.TotalSeconds, 2) }
            $report.candidateObserved = $true
        }
        if ($report.controlObserved -and $report.candidateObserved) { break }
        Start-Sleep -Milliseconds 500
    } while ($timer.Elapsed.TotalSeconds -lt 90)
    if ($observed) {
        Start-Sleep -Seconds 15
        $report.candidateStillAliveAfter15Seconds = -not $observed.HasExited
    }
    $events = @(Get-Event -SourceIdentifier $eventName -ErrorAction SilentlyContinue)
    $report.eventCreationCount = $events.Count
    $report.observationSeconds = [Math]::Round($timer.Elapsed.TotalSeconds, 2)
    if ($dispatcher.HasExited) { $report.dispatcherExitCode = $dispatcher.ExitCode }
    $report.result = if ($report.controlObserved -and $report.candidateObserved) { 'candidate-launched-via-dispatch-experiment' }
        elseif ($report.controlObserved) { 'candidate-not-observed-despite-control' }
        elseif ($report.candidateObserved) { 'candidate-observed-control-missing' }
        else { 'inconclusive-dispatch-control-not-observed' }
} catch {
    $report.result = 'harness-error'
    $report.errorType = $_.Exception.GetType().Name
} finally {
    if ($registered) {
        Remove-ItemProperty -LiteralPath $runPath -Name $candidateName -ErrorAction SilentlyContinue
        Remove-ItemProperty -LiteralPath $runPath -Name $controlName -ErrorAction SilentlyContinue
        $remaining = Get-Item -LiteralPath $runPath
        $report.testRegistrationsRemoved = $remaining.GetValueNames() -notcontains $candidateName -and $remaining.GetValueNames() -notcontains $controlName
    }
    foreach ($child in @(Get-Process -Name CodexUsageTray -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $candidate })) {
        $child.Kill($true)
        [void]$child.WaitForExit(10000)
    }
    if ($dispatcher -and -not $dispatcher.HasExited) { $dispatcher.Kill(); [void]$dispatcher.WaitForExit(10000) }
    if ($subscription) {
        Unregister-Event -SourceIdentifier $eventName
        Get-Event -SourceIdentifier $eventName -ErrorAction SilentlyContinue | Remove-Event
    }
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $env:RUNNER_TEMP 'startup-dispatch-report.json')
    Write-Host ('Dispatcher observation: ' + $report.result)
    if ($env:GITHUB_STEP_SUMMARY) {
        "`n## Startup-dispatch experiment`n$($report.result). This is not Windows sign-in evidence." | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY
    }
}
if ($report.result -eq 'harness-error') { exit 1 }

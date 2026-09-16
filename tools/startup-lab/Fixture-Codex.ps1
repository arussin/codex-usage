# A local JSONL test double. No account, network call or personal data is used.
$ErrorActionPreference = 'Stop'
while ($null -ne ($line = [Console]::ReadLine())) {
    $request = $line | ConvertFrom-Json
    if ($request.method -eq 'initialize') {
        [Console]::WriteLine('{"id":1,"result":{"userAgent":"synthetic-startup-lab"}}')
    } elseif ($request.method -eq 'account/rateLimits/read') {
        $mode = [IO.File]::ReadAllText($env:CODEX_LAB_MODE).Trim()
        if ($mode -eq 'error') {
            $response = '{"id":2,"error":{"code":-32000,"message":"Synthetic offline condition"}}'
        } else {
            $now = [DateTimeOffset]::UtcNow
            $response = @{
                id = 2
                result = @{
                    rateLimits = @{
                        primary = @{ usedPercent = 25; windowDurationMins = 300; resetsAt = $now.AddHours(1).ToUnixTimeSeconds() }
                        secondary = @{ usedPercent = 40; windowDurationMins = 10080; resetsAt = $now.AddDays(7).ToUnixTimeSeconds() }
                    }
                }
            } | ConvertTo-Json -Depth 8 -Compress
        }
        # The marker proves that the real app requested a refresh from this fixture.
        [IO.File]::AppendAllText($env:CODEX_LAB_CALLS, $mode + "`n")
        [Console]::WriteLine($response)
        [Console]::Out.Flush()
    }
}

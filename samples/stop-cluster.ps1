$statePath = Join-Path $PSScriptRoot ".crowcast-sample-processes.json"
if (-not (Test-Path -LiteralPath $statePath)) {
    Write-Host "No sample processes are registered."
    return
}

$processes = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
foreach ($entry in $processes) {
    $process = Get-Process -Id $entry.Id -ErrorAction SilentlyContinue
    if ($null -ne $process) { Stop-Process -Id $entry.Id }
}
Remove-Item -LiteralPath $statePath
Write-Host "CrowCast sample processes stopped."

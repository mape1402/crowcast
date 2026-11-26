param(
    [string]$Configuration = "Debug",
    [ValidateRange(1, 50)]
    [int]$ReplicaCount = 10
)

$sampleRoot = Split-Path -Parent $PSScriptRoot
$statePath = Join-Path $PSScriptRoot ".crowcast-sample-processes.json"
if (Test-Path -LiteralPath $statePath) {
    throw "The sample state file already exists. Run stop-cluster.ps1 first."
}

dotnet build (Join-Path $sampleRoot "CrowCast.sln") -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "The sample build failed." }

$processes = @()
$controlPlaneAssembly = Join-Path $PSScriptRoot "CrowCast.ControlPlane.Sample\bin\$Configuration\net10.0\CrowCast.ControlPlane.Sample.dll"
$controlPlane = Start-Process dotnet -WindowStyle Hidden -PassThru -ArgumentList @("`"$controlPlaneAssembly`"")
$processes += [pscustomobject]@{ Name = "control-plane"; Id = $controlPlane.Id }

Start-Sleep -Seconds 2
$replicaAssembly = Join-Path $PSScriptRoot "CrowCast.WebUI.Sample\bin\$Configuration\net10.0\CrowCast.WebUI.Sample.dll"
for ($index = 0; $index -lt $ReplicaCount; $index++) {
    $webPort = 5201 + $index
    $gossipPort = 4053 + $index
    $arguments = @(
        "`"$replicaAssembly`"",
        "--urls", "http://127.0.0.1:$webPort",
        "--gossip-port", "$gossipPort",
        "--seed-port", "4053",
        "--control-plane", "http://127.0.0.1:5081"
    )
    $replica = Start-Process dotnet -WindowStyle Hidden -PassThru -ArgumentList $arguments
    $processes += [pscustomobject]@{ Name = "replica-$($index + 1)"; Id = $replica.Id; Dashboard = "http://127.0.0.1:$webPort/crowcast" }
}

$processes | ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding utf8
Write-Host "Control plane: http://127.0.0.1:5080/crowcast-control"
$processes | Where-Object Dashboard | ForEach-Object { Write-Host "$($_.Name): $($_.Dashboard)" }

param([string]$Configuration = "Debug")

$statePath = Join-Path $PSScriptRoot ".crowcast-sample-processes.json"
if (-not (Test-Path -LiteralPath $statePath)) {
    throw "Run run-cluster.ps1 first so the shared control plane is available."
}

$sampleRoot = Split-Path -Parent $PSScriptRoot
$assembly = Join-Path $PSScriptRoot "CrowCast.WebUI.Sample\bin\$Configuration\net10.0\CrowCast.WebUI.Sample.dll"
if (-not (Test-Path -LiteralPath $assembly)) {
    throw "The sample assembly is missing. Build CrowCast.sln first."
}

$definitions = @(
    [pscustomobject]@{ Cluster = "crowcast-dashboard-sample-3"; ReplicaCount = 3; WebStart = 5211; GossipStart = 4063 },
    [pscustomobject]@{ Cluster = "crowcast-dashboard-sample-5"; ReplicaCount = 5; WebStart = 5214; GossipStart = 4066 }
)
$ports = @(5211..5218) + @(4063..4070)
$listeners = Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
    Where-Object { $_.LocalPort -in $ports }
if ($listeners) {
    throw "At least one extra-cluster port is already in use."
}

$processes = @(Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json)
if ($processes | Where-Object { $_.Cluster -in $definitions.Cluster }) {
    throw "The extra clusters are already registered."
}

$started = @()
try {
    foreach ($definition in $definitions) {
        for ($index = 0; $index -lt $definition.ReplicaCount; $index++) {
            $webPort = $definition.WebStart + $index
            $gossipPort = $definition.GossipStart + $index
            $arguments = @(
                "`"$assembly`"",
                "--urls", "http://127.0.0.1:$webPort",
                "--gossip-port", "$gossipPort",
                "--seed-port", "$($definition.GossipStart)",
                "--cluster", $definition.Cluster,
                "--service", "crowcast-webui-sample",
                "--control-plane", "http://127.0.0.1:5081"
            )
            $replica = Start-Process dotnet -WindowStyle Hidden -PassThru -ArgumentList $arguments
            $entry = [pscustomobject]@{
                Name = "$($definition.Cluster)-replica-$($index + 1)"
                Id = $replica.Id
                Dashboard = "http://127.0.0.1:$webPort/crowcast"
                Cluster = $definition.Cluster
            }
            $started += $entry
            $processes += $entry
        }
    }

    $processes | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $statePath -Encoding utf8
}
catch {
    $started | ForEach-Object { Stop-Process -Id $_.Id -ErrorAction SilentlyContinue }
    throw
}

Write-Host "3-replica cluster: dashboards 5211 through 5213"
Write-Host "5-replica cluster: dashboards 5214 through 5218"

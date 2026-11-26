param([string]$Configuration = "Debug")

& (Join-Path $PSScriptRoot "run-cluster.ps1") -Configuration $Configuration -ReplicaCount 5

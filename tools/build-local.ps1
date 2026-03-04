param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (Test-Path dist) { Remove-Item dist -Recurse -Force }
New-Item dist -ItemType Directory | Out-Null

nuget restore .\transaq-nt8-bridge.sln
msbuild .\transaq-nt8-bridge.sln /p:Configuration=$Configuration /m

$gatewayOut = Join-Path $root "src\TransaqGateway\bin\$Configuration\net48"
$gatewayStage = Join-Path $root "dist\TransaqGateway"
New-Item $gatewayStage -ItemType Directory | Out-Null
Copy-Item "$gatewayOut\*" $gatewayStage -Recurse -Force
Copy-Item ".\config\config.template.json" "$gatewayStage\config.template.json" -Force
Compress-Archive -Path "$gatewayStage\*" -DestinationPath ".\dist\TransaqGateway.zip" -Force

$addonStageRoot = Join-Path $root "dist\NT8_AddOn_Source"
$addonSource = Join-Path $addonStageRoot "AddOns\TransaqBridge"
New-Item $addonSource -ItemType Directory -Force | Out-Null
Copy-Item ".\src\NinjaTrader8.AddOn.TransaqBridge\*.cs" $addonSource -Force
Copy-Item ".\src\NinjaTrader8.AddOn.TransaqBridge\NinjaScript\AddOns\*.cs" $addonSource -Force
Compress-Archive -Path "$addonStageRoot\*" -DestinationPath ".\dist\NT8_AddOn_Source.zip" -Force

Write-Host "Done. Artifacts in dist/."

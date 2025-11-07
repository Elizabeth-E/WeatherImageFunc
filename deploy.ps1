#!/usr/bin/env pwsh
# Requires: PowerShell 7+, Azure CLI, .NET 8 SDK
# Usage:
#   az login
#   az account set --subscription "<YOUR SUB ID OR NAME>"
#   ./deploy.ps1

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ------------------ CONFIG ------------------
$ResourceGroup   = "rg-weatherapp-ssp1"
$Location        = "westeurope"

$StorageName     = "weatherappstoragessp1"
$FunctionAppName = "weatherimagefunctionssp1"
$AppInsightsName = "weatherimagefunctionssp1-ai"

$BicepFile       = "main.bicep"
$CsprojPath      = (Resolve-Path "WeatherImageFunc.csproj").Path
# ------------------------------------------------

Write-Host ">>> Checking Azure CLI context..."
$acct = az account show --only-show-errors | ConvertFrom-Json
if (-not $acct) { throw "Not logged in. Run 'az login' first." }
Write-Host "    Subscription:" $acct.name "("$acct.id")"

# 1) Resource group
Write-Host ">>> Ensuring resource group '$ResourceGroup' in '$Location' ..."
az group create -g $ResourceGroup -l $Location --only-show-errors | Out-Null

# 2) Deploy infra (Bicep)
Write-Host ">>> Deploying infrastructure via $BicepFile ..."
$deploy = az deployment group create `
  -g $ResourceGroup `
  -f $BicepFile `
  -p location=$Location `
     storageAccountName=$StorageName `
     functionAppName=$FunctionAppName `
     appInsightsName=$AppInsightsName `
  --only-show-errors | ConvertFrom-Json

if (-not $deploy) { throw "Bicep deployment failed." }

# Use Bicep output to get the actual Function App name
$FunctionAppName = $deploy.properties.outputs.functionAppName.value
$faHost = $deploy.properties.outputs.functionDefaultHostname.value
Write-Host "    Function App deployed: $FunctionAppName"
Write-Host "    Function default host:" $faHost

# 3) Build & package the Functions app
Write-Host ">>> Publishing Functions project ($CsprojPath) ..."
dotnet publish $CsprojPath -c Release

$publishDir = Join-Path (Split-Path -Path $CsprojPath -Parent) "bin/Release/net8.0/publish"
if (-not (Test-Path $publishDir)) { throw "Publish folder not found: $publishDir" }

$zipPath = Join-Path $publishDir "app.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

# 4) Cross-platform zipping
Write-Host ">>> Zipping package: $zipPath"
Push-Location $publishDir
if ($IsWindows) {
    Compress-Archive -Path * -DestinationPath $zipPath -Force
} else {
    zip -r $zipPath ./*
}
Pop-Location

# 5) Wait for Function App to initialize
Write-Host ">>> Waiting for Function App to be ready..."
Start-Sleep -Seconds 120

# 6) Deploy code to Function App
Write-Host ">>> Deploying package to Azure Function App '$FunctionAppName' ..."
az functionapp deployment source config-zip `
  -g $ResourceGroup `
  -n $FunctionAppName `
  --src $zipPath `
  --only-show-errors | Out-Null

Write-Host ""
Write-Host "   Deploy complete."
Write-Host "   Resource Group : $ResourceGroup"
Write-Host "   Function App   : $FunctionAppName"
Write-Host "   Hostname       : $faHost"
Write-Host ""
Write-Host "Common endpoints (adjust to your function names):"
Write-Host ("   Base:       https://{0}/api" -f $faHost)
Write-Host ("   Example:    https://{0}/api/start" -f $faHost)

# 7) Optional: list deployed functions
Write-Host ">>> Listing deployed functions..."
az functionapp function list `
  --name $FunctionAppName `
  --resource-group $ResourceGroup `
  --query "[].{name:name, url:invokeUrlTemplate}" `
  -o table
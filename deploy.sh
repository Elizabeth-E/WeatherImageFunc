#!/usr/bin/env bash
# Requires: bash, Azure CLI, .NET 8 SDK, zip, jq
# Usage: ./deploy.sh
# Ensures reliable deploy for Linux

set -euo pipefail

# ------------------ CONFIG ------------------
RESOURCE_GROUP="rg-weatherapp-ssp1"
LOCATION="westeurope"

STORAGE_NAME="weatherappstoragessp1"
FUNCTION_APP_NAME="weatherimagefunctionssp1"
APP_INSIGHTS_NAME="weatherimagefunctionssp1-ai"

BICEP_FILE="main.bicep"
CSPROJ_PATH="WeatherImageFunc.csproj"
# ------------------------------------------------

echo ">>> Checking Azure CLI context..."
az account show --only-show-errors > /dev/null

echo ">>> Ensuring resource group $RESOURCE_GROUP ..."
az group create -g "$RESOURCE_GROUP" -l "$LOCATION" --only-show-errors > /dev/null

echo ">>> Deploying infrastructure via $BICEP_FILE ..."
DEPLOY_JSON=$(az deployment group create \
  -g "$RESOURCE_GROUP" \
  -f "$BICEP_FILE" \
  -p location="$LOCATION" \
     storageAccountName="$STORAGE_NAME" \
     functionAppName="$FUNCTION_APP_NAME" \
     appInsightsName="$APP_INSIGHTS_NAME" \
  --only-show-errors --query "properties.outputs" -o json)

FUNCTION_APP_NAME=$(echo "$DEPLOY_JSON" | jq -r '.functionAppName.value')
FA_HOST=$(echo "$DEPLOY_JSON" | jq -r '.functionDefaultHostname.value')
echo "    Function App deployed: $FUNCTION_APP_NAME"
echo "    Function default host: $FA_HOST"

echo ">>> Publishing Functions project ($CSPROJ_PATH) ..."
dotnet publish "$CSPROJ_PATH" -c Release

PUBLISH_DIR=$(dirname "$CSPROJ_PATH")/bin/Release/net8.0/publish
ZIP_PATH="$PUBLISH_DIR/app.zip"

# Ensure publish folder exists
mkdir -p "$PUBLISH_DIR"

echo ">>> Creating zip package: $ZIP_PATH"
# Remove old zip if it exists
[ -f "$ZIP_PATH" ] && rm -f "$ZIP_PATH"

# Zip contents of publish folder
cd "$PUBLISH_DIR"
zip -r "$ZIP_PATH" ./*

# Return to project root
cd - > /dev/null

echo ">>> Waiting briefly for Function App initialization..."
sleep 30

echo ">>> Deploying package to Azure Function App '$FUNCTION_APP_NAME' ..."
az functionapp deployment source config-zip \
  -g "$RESOURCE_GROUP" \
  -n "$FUNCTION_APP_NAME" \
  --src "$ZIP_PATH" \
  --only-show-errors

echo ""
echo "   Deploy complete."
echo "   Resource Group : $RESOURCE_GROUP"
echo "   Function App   : $FUNCTION_APP_NAME"
echo "   Hostname       : $FA_HOST"
echo ""
echo "Common endpoints (adjust to your function names):"
echo "   Base:    https://$FA_HOST/api"
echo "   Example: https://$FA_HOST/api/start"

echo ">>> Listing deployed functions..."
az functionapp function list \
  --name "$FUNCTION_APP_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --query "[].{name:name, url:invokeUrlTemplate}" \
  -o table
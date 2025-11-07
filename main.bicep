@description('Location for all resources')
param location string = resourceGroup().location

@description('Globally-unique name for the Storage Account (3-24 lower case letters/numbers)')
param storageAccountName string

@description('Name of the Function App (must be globally unique)')
param functionAppName string

@description('Name of the Application Insights resource')
param appInsightsName string

@description('Pexels API key (if you use Pexels). Leave empty to set later.')
@secure()
param pexelsApiKey string = ''

@description('Output container name for generated images')
param outputBlobContainerName string = 'weather-images'

@description('Background cache container (for source images)')
param backgroundCacheContainerName string = 'background-cache'

@description('Buienradar API endpoint')
param buienradarApi string = 'https://data.buienradar.nl/2.0/feed/json'

/* ------------------------------
   Storage account
-------------------------------- */
resource sa 'Microsoft.Storage/storageAccounts@2023-04-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
  }
}

/* Blob service (existing handle) */
resource blobSvc 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' existing = {
  parent: sa
  name: 'default'
}

/* Blob containers */
resource imagesContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobSvc
  name: outputBlobContainerName
  properties: {
    publicAccess: 'None' // keep private; you can use SAS when serving
  }
}

resource cacheContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobSvc
  name: backgroundCacheContainerName
  properties: {
    publicAccess: 'None'
  }
}

/* Queue service (existing handle) */
resource queueSvc 'Microsoft.Storage/storageAccounts/queueServices@2023-01-01' existing = {
  parent: sa
  name: 'default'
}

/* Queues */
resource qStart 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-01-01' = {
  parent: queueSvc
  name: 'image-start'
}

resource qProcess 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-01-01' = {
  parent: queueSvc
  name: 'image-process'
}

/* ------------------------------
   Application Insights
-------------------------------- */
resource ai 'microsoft.insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    IngestionMode: 'ApplicationInsights'
  }
}

/* ------------------------------
   Consumption plan (Linux)
-------------------------------- */
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${functionAppName}-plan'
  location: location
  kind: 'functionapp'
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  properties: {
    reserved: true // Linux
  }
}

/* ------------------------------
   Function App (Linux, dotnet isolated)
-------------------------------- */
var storageKey = listKeys(sa.id, '2022-09-01').keys[0].value
var storageConn = 'DefaultEndpointsProtocol=https;AccountName=${sa.name};AccountKey=${storageKey};EndpointSuffix=${environment().suffixes.storage}'

resource fa 'Microsoft.Web/sites@2023-12-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  properties: {
    httpsOnly: true
    serverFarmId: plan.id
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|8.0'
      appSettings: [
        { name: 'AzureWebJobsStorage', value: storageConn }
        { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
        { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
        { name: 'WEBSITE_RUN_FROM_PACKAGE', value: '1' }

        // Assignment-related config
        { name: 'OUTPUT_BLOB_CONTAINER', value: outputBlobContainerName }
        { name: 'BUIENRADAR_API', value: buienradarApi }
        { name: 'PEXELS_API_KEY', value: pexelsApiKey }

        // App Insights
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: ai.properties.ConnectionString }
        // Cost control: 5% sampling
        { name: 'APPLICATIONINSIGHTS_SAMPLING_PERCENTAGE', value: '5' }
      ]
      alwaysOn: false
      ftpsState: 'FtpsOnly'
    }
  }
}

/* ------------------------------
   Outputs
-------------------------------- */
output functionAppName string = fa.name
output functionDefaultHostname string = fa.properties.defaultHostName
output storageAccount string = sa.name
output outputContainer string = imagesContainer.name
output startQueue string = qStart.name
output processQueue string = qProcess.name
output appInsights string = ai.name

// ─────────────────────────────────────────────────────────────────────────────
//  KenoKoch.de – Azure-Infrastruktur
//  App Service (Linux, .NET 10) · Azure SQL Database (Serverless, Free Offer)
//  Application Insights · Managed Identity · optional eigene Domain mit Zertifikat
//
//  Bereitstellung:
//    az group create -n rg-kenokoch -l westeurope
//    az deployment group create -g rg-kenokoch -f infra/main.bicep -p infra/main.bicepparam
// ─────────────────────────────────────────────────────────────────────────────
targetScope = 'resourceGroup'

@description('Kurzname für alle Ressourcen (Kleinbuchstaben und Ziffern).')
@minLength(3)
@maxLength(14)
param appName string = 'kenokoch'

@description('Azure-Region aller Ressourcen.')
param location string = resourceGroup().location

@description('Tarif des App Service Plans. B1 ist der kleinste Tarif mit eigener Domain, verwaltetem Zertifikat und Always On.')
@allowed([
  'B1'
  'B2'
  'S1'
  'P0v3'
  'P1v3'
])
param appServiceSku string = 'B1'

@description('Anmeldename des Microsoft-Entra-Administrators für SQL (z. B. die eigene E-Mail-Adresse).')
param sqlAdminLogin string

@description('Objekt-ID des Microsoft-Entra-Administrators (Benutzer oder Gruppe).')
param sqlAdminObjectId string

@description('Art des SQL-Administrators.')
@allowed([
  'User'
  'Group'
])
param sqlAdminPrincipalType string = 'User'

@description('Kostenloses Azure-SQL-Angebot nutzen (monatliches Freikontingent, eine Datenbank pro Abonnement).')
param useSqlFreeLimit bool = true

@description('Verhalten, wenn das Freikontingent aufgebraucht ist: pausieren (kostenlos) oder weiterlaufen und abrechnen.')
@allowed([
  'AutoPause'
  'BillOverUsage'
])
param sqlFreeLimitExhaustionBehavior string = 'AutoPause'

@description('Eigene Domain ohne www, z. B. "kenokoch.de". Leer lassen, bis die DNS-Einträge gesetzt sind.')
param customDomain string = ''

@description('Zusätzlich www.<Domain> binden (leitet die App auf die Hauptdomain um).')
param includeWww bool = true

var suffix = take(uniqueString(resourceGroup().id), 6)
var names = {
  logAnalytics: 'log-${appName}'
  appInsights: 'appi-${appName}'
  plan: 'asp-${appName}'
  site: 'app-${appName}-${suffix}'
  sqlServer: 'sql-${appName}-${suffix}'
  sqlDatabase: 'sqldb-${appName}'
}
var tags = {
  application: 'KenoKoch.de'
  managedBy: 'bicep'
}

// ── Beobachtbarkeit ─────────────────────────────────────────────────────────
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: names.logAnalytics
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    workspaceCapping: {
      dailyQuotaGb: 1 // Kostenbremse
    }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: names.appInsights
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
    DisableIpMasking: false
  }
}

// ── Datenbank: Azure SQL Serverless, nur Microsoft-Entra-Anmeldung ───────────
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: names.sqlServer
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: sqlAdminLogin
      sid: sqlAdminObjectId
      principalType: sqlAdminPrincipalType
      tenantId: subscription().tenantId
    }
  }
}

// Erlaubt Verbindungen aus Azure-Diensten (App Service). Keine öffentlichen IP-Bereiche.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: names.sqlDatabase
  location: location
  tags: tags
  sku: {
    name: 'GP_S_Gen5_2'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: union(
    {
      autoPauseDelay: 60
      minCapacity: json('0.5')
      maxSizeBytes: 34359738368 // 32 GB
      zoneRedundant: false
      requestedBackupStorageRedundancy: 'Local'
    },
    useSqlFreeLimit
      ? {
          useFreeLimit: true
          freeLimitExhaustionBehavior: sqlFreeLimitExhaustionBehavior
        }
      : {}
  )
}

// ── App Service ──────────────────────────────────────────────────────────────
resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: names.plan
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: appServiceSku
  }
  properties: {
    reserved: true // Linux
  }
}

var publicBaseUrl = empty(customDomain) ? 'https://${names.site}.azurewebsites.net' : 'https://${customDomain}'
var sqlConnectionString = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${names.sqlDatabase};Authentication=Active Directory Managed Identity;Encrypt=True;TrustServerCertificate=False;Connect Timeout=60;'

resource site 'Microsoft.Web/sites@2024-04-01' = {
  name: names.site
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned' // passwortloser Zugriff auf Azure SQL
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: true // Blazor Server braucht Sticky Sessions, sobald mehrere Instanzen laufen
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      appCommandLine: 'dotnet KenoKoch.Web.dll'
      alwaysOn: true
      webSocketsEnabled: true // SignalR für Blazor Server
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/health' // Liveness ohne Datenbank: weckt die Serverless-Datenbank nicht
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED'
          value: 'true'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'Site__BaseUrl'
          value: publicBaseUrl
        }
        {
          name: 'Site__CanonicalHost'
          value: customDomain
        }
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '1'
        }
      ]
      connectionStrings: [
        {
          name: 'Sql'
          type: 'SQLAzure'
          connectionString: sqlConnectionString
        }
      ]
    }
  }
}

// ── Eigene Domain (optional) ─────────────────────────────────────────────────
module apexDomain 'modules/customDomain.bicep' = if (!empty(customDomain)) {
  name: 'domain-apex'
  params: {
    siteName: site.name
    location: location
    planId: plan.id
    domainName: customDomain
  }
}

module wwwDomain 'modules/customDomain.bicep' = if (!empty(customDomain) && includeWww) {
  name: 'domain-www'
  params: {
    siteName: site.name
    location: location
    planId: plan.id
    domainName: 'www.${customDomain}'
  }
  dependsOn: [
    apexDomain
  ]
}

// ── Ausgaben ─────────────────────────────────────────────────────────────────
@description('Name der Web App (für GitHub-Variable AZURE_WEBAPP_NAME und das SQL-Skript).')
output webAppName string = site.name

@description('Standard-Hostname der Web App.')
output webAppHostName string = site.properties.defaultHostName

@description('Objekt-ID der Managed Identity der Web App.')
output webAppPrincipalId string = site.identity.principalId

@description('Wert für den TXT-Eintrag "asuid" bzw. "asuid.www" beim DNS-Anbieter.')
output customDomainVerificationId string = site.properties.customDomainVerificationId

@description('Vollqualifizierter Name des SQL Servers.')
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName

@description('Name der Datenbank.')
output sqlDatabaseName string = sqlDatabase.name

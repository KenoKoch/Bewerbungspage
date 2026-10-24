// Aktiviert TLS (SNI) für eine bereits gebundene Domain.

@description('Name der Web App.')
param siteName string

@description('Gebundene Domain.')
param domainName string

@description('Fingerabdruck des verwalteten Zertifikats.')
param thumbprint string

resource site 'Microsoft.Web/sites@2024-04-01' existing = {
  name: siteName
}

resource binding 'Microsoft.Web/sites/hostNameBindings@2024-04-01' = {
  parent: site
  name: domainName
  properties: {
    siteName: siteName
    hostNameType: 'Verified'
    sslState: 'SniEnabled'
    thumbprint: thumbprint
  }
}

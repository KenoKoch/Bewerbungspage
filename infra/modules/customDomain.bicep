// Bindet eine eigene Domain an die Web App und stellt ein kostenloses, von Azure verwaltetes TLS-Zertifikat aus.
// Voraussetzung: DNS-Einträge sind gesetzt (TXT "asuid[.www]" + A-Eintrag bzw. CNAME), siehe README.

@description('Name der Web App.')
param siteName string

@description('Region der Web App.')
param location string

@description('Resource-ID des App Service Plans.')
param planId string

@description('Domain, z. B. "kenokoch.com" oder "www.kenokoch.com".')
param domainName string

resource site 'Microsoft.Web/sites@2024-04-01' existing = {
  name: siteName
}

// 1. Hostnamen binden (noch ohne TLS)
resource hostNameBinding 'Microsoft.Web/sites/hostNameBindings@2024-04-01' = {
  parent: site
  name: domainName
  properties: {
    siteName: siteName
    hostNameType: 'Verified'
    sslState: 'Disabled'
  }
}

// 2. Verwaltetes Zertifikat ausstellen lassen
resource certificate 'Microsoft.Web/certificates@2024-04-01' = {
  name: '${domainName}-${siteName}'
  location: location
  properties: {
    serverFarmId: planId
    canonicalName: domainName
  }
  dependsOn: [
    hostNameBinding
  ]
}

// 3. Bindung mit SNI-Zertifikat aktualisieren (eigene Bereitstellung, da dieselbe Ressource erneut geschrieben wird)
module sniBinding 'sniBinding.bicep' = {
  name: 'sni-${uniqueString(domainName)}'
  params: {
    siteName: siteName
    domainName: domainName
    thumbprint: certificate.properties.thumbprint
  }
}

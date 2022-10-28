param staticWebAppName string
param location string
param appTags object = {}
param sku object = {
  name: 'Free'
  tier: 'Free'
}

resource web 'Microsoft.Web/staticSites@2022-03-01' = {
  name: staticWebAppName
  location: location
  tags: union(appTags, {
    'azd-service-name': 'AuthorizeWebApp'
  })
  sku: sku
  properties: {
    provider: 'Custom'
  }
}

output name string = web.name
output uri string = 'https://${web.properties.defaultHostname}'

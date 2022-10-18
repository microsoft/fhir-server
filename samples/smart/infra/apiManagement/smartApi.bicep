param apiManagementServiceName string
param fhirBaseUrl string
param apimServiceLoggerId string

resource smartApi 'Microsoft.ApiManagement/service/apis@2021-12-01-preview' = {
  name: '${apiManagementServiceName}/smartv1'
  properties: {
    displayName: 'SMART v1'
    apiRevision: 'v1'
    subscriptionRequired: false
    serviceUrl: fhirBaseUrl
    protocols: [
      'https'
    ]
    path: '/smart'
  }

  resource metadataOverrideOperation 'operations' = {
    name: 'metadatOverride'
    properties: {
      displayName: '/metadata'
      method: 'GET'
      urlTemplate: '/metadata'
    }

    resource metadataOverrideOperationPolicy 'policies' = {
      name: 'policy'
      properties: {
        format: 'rawxml'
        value: loadTextContent('policies/metadataOverrideOperationPolicy.xml')
      }
    }
  }

  resource smartWellKnownOperation 'operations' = {
    name: 'smartWellKnown'
    properties: {
      displayName: 'SMART well-known endpoint'
      method: 'GET'
      urlTemplate: '/.well-known/smart-configuration'
    }

    resource smartWellKnownOperationPolicy 'policies' = {
      name: 'policy'
      properties: {
        format: 'rawxml'
        value: loadTextContent('policies/smartWellKnownOperationPolicy.xml')
      }
    }
  }

  resource smartAuthorizeEndpoint 'operations' = {
    name: 'smartAuthorizeEndpoint'
    properties: {
      displayName: 'SMART Authorize Endpoint (GET)'
      method: 'GET'
      urlTemplate: '/authorize'
    }

    resource smartAuthorizeEndpointPolicy 'policies' = {
      name: 'policy'
      properties: {
        format: 'rawxml'
        value: loadTextContent('policies/authorizeEndpointPolicy.xml')
      }
    }
  }

  resource smartTokenEndpoint 'operations' = {
    name: 'smartTokenEndpoint'
    properties: {
      displayName: 'SMART Token Endpoint'
      method: 'POST'
      urlTemplate: '/token'
    }

    resource smartAuthorizeEndpointPolicy 'policies' = {
      name: 'policy'
      properties: {
        format: 'rawxml'
        value: loadTextContent('policies/tokenEndpointPolicy.xml')
      }
    }
  }

  resource allOtherRequestsOperations 'operations' = {
    name: 'allOtherRequests'
    properties: {
      displayName: 'all-other-operations'
      method: 'GET'
      urlTemplate: '/*'
    }
  }

  resource smartApiDiagnostics 'diagnostics' = {
    name: 'applicationinsights'
    properties: {
      alwaysLog: 'allErrors'
      httpCorrelationProtocol: 'W3C'
      verbosity: 'information'
      logClientIp: true
      loggerId: apimServiceLoggerId
      sampling: {
        samplingType: 'fixed'
        percentage: 100
      }
    }
  }
}

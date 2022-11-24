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

  resource smartAppConsentInfoOptions 'operations' = {
    name: 'smartAppConsentInfoEndpointOptions'
    properties: {
      displayName: 'SMART Consent Info (OPTIONS)'
      method: 'OPTIONS'
      urlTemplate: '/appConsentInfo'
    }

    resource smartAuthorizeEndpointPolicy 'policies' = {
      name: 'policy'
      properties: {
        format: 'rawxml'
        value: loadTextContent('policies/appConsentInfoEndpointPolicy.xml')
      }
    }
  }

  resource smartAppConsentInfoGet 'operations' = {
    name: 'smartAppConsentInfoEndpointGet'
    properties: {
      displayName: 'SMART Consent Info (GET)'
      method: 'GET'
      urlTemplate: '/appConsentInfo'
    }

    resource smartAuthorizeEndpointPolicy 'policies' = {
      name: 'policy'
      properties: {
        format: 'rawxml'
        value: loadTextContent('policies/appConsentInfoEndpointPolicy.xml')
      }
    }
  }

  resource smartAppConsentInfoPost 'operations' = {
    name: 'smartAppConsentInfoEndpointPost'
    properties: {
      displayName: 'SMART Consent Info (POST)'
      method: 'POST'
      urlTemplate: '/appConsentInfo'
    }

    resource smartAuthorizeEndpointPolicy 'policies' = {
      name: 'policy'
      properties: {
        format: 'rawxml'
        value: loadTextContent('policies/appConsentInfoEndpointPolicy.xml')
      }
    }
  }

  resource smart 'operations' = {
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

  resource exportStatusCheckEndpoint 'operations' = {
    name: 'exportStatusCheck'
    properties: {
      displayName: 'Export Check Status'
      method: 'GET'
      urlTemplate: '/_operations/export/{exportId}'
      templateParameters: [
        {
          name: 'exportId'
          description: 'Identifier of the $export operation'
          required: true
          type: 'SecureString'
        }
      ]
    }

    resource exportStatusCheckEndpointPolicy 'policies' = {
      name: 'policy'
      properties: {
        format: 'rawxml'
        value: loadTextContent('./policies/exportCheck.xml')
      }
    }
  }

  resource exportGetDataEndpoint 'operations' = {
    name: 'getExportedData'
    properties: {
      displayName: 'GET Exported Data'
      method: 'GET'
      urlTemplate: '/_export/{containerName}/{folderName}/{fileName}'
      templateParameters: [
        {
          name: 'containerName'
          description: 'Name of the export storage container. Must match the object id of the token'
          required: true
          type: 'SecureString'
        }
        {
          name: 'folderName'
          required: true
          type: 'SecureString'
        }
        {
          name: 'fileName'
          required: true
          type: 'SecureString'
        }
      ]
    }

    resource exportStatusCheckEndpointPolicy 'policies' = {
      name: 'policy'
      properties: {
        format: 'rawxml'
        value: loadTextContent('./policies/exportGetData.xml')
      }
    }
  }

  resource groupExportEndpoint 'operations' = {
    name: 'groupExport'
    properties: {
      displayName: 'Export Group'
      method: 'GET'
      urlTemplate: '/Group/{logicalId}/$export'
      templateParameters: [
        {
          name: 'logicalId'
          description: 'ID of the group to export'
          required: true
          type: 'SecureString'
        }
      ]
    }

    resource exportStatusCheckEndpointPolicy 'policies' = {
      name: 'policy'
      properties: {
        format: 'rawxml'
        value: loadTextContent('./policies/groupExport.xml')
      }
    }
  }

  resource deleteExportEndpoint 'operations' = {
    name: 'deleteExport'
    properties: {
      displayName: 'Export Delete'
      method: 'DELETE'
      urlTemplate: '/_operations/export/{exportId}'
      templateParameters: [
        {
          name: 'exportId'
          description: 'ID of the export to delete'
          required: true
          type: 'SecureString'
        }
      ]
    }

    resource exportStatusCheckEndpointPolicy 'policies' = {
      name: 'policy'
      properties: {
        format: 'rawxml'
        value: loadTextContent('./policies/groupExport.xml')
      }
    }
  }

  resource allOtherRequestsOperationsGet 'operations' = {
    name: 'allOtherRequestsGet'
    properties: {
      displayName: 'all-other-operations GET'
      method: 'GET'
      urlTemplate: '/*'
    }
  }

  resource allOtherRequestsOperationsPost 'operations' = {
    name: 'allOtherRequestsPost'
    properties: {
      displayName: 'all-other-operations POST'
      method: 'POST'
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

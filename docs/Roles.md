# Roles in Microsoft FHIR Server for Azure

The FHIR server uses a role based access control system. The privileges (Read, Write, etc.) assigned to specific roles are defined with an array structure:

```json
[
    {
        "name": "clinician",
        "resourcePermissions": [
            {
                "actions": [
                    "Read",
                    "Write"
                ]
            }
        ]
    },
    {
        "name": "researcher",
        "resourcePermissions": [
            {
                "actions": [
                    "Read"
                ]
            }
        ]
    },
    {
        "name": "admin",
        "resourcePermissions": [
            {
                "actions": [
                    "Read",
                    "Write",
                    "HardDelete"
                ]
            }
        ]
    }
]
```

This structure is passed to the FHIR server at startup and enforced using the `roles` claim in the JWT access token presented when cusuming the FHIR server API. 

When deploying the FHIR server into Azure using the provided [resource manager template](../samples/templates/default-azuredeploy.json) the array of roles can be passed in the `additionalFhirServerConfigProperties` parameter, which will add the roles to the [App Settings](https://docs.microsoft.com/en-us/azure/app-service/web-sites-configure) of the front end [Web App](https://azure.microsoft.com/en-us/services/app-service/web/) running the server. 

In the app settings the nested array structure must be flattened and added to the `FhirServer:Security:Authorization:Roles` section of the configuration. Specifically, a roles array like:

```json
[
    {
        "name": "admin",
        "resourcePermissions": [
            {
                "actions": [
                    "Read",
                    "Write",
                    "HardDelete"
                ]
            }
        ]
    }
]
```

would become:

```json
{
    "FhirServer:Security:Authorization:Roles:0:name": "admin",
    "FhirServer:Security:Authorization:Roles:0:resourcePermissions:0:actions:0": "Read",
    "FhirServer:Security:Authorization:Roles:0:resourcePermissions:0:actions:1": "Write",
    "FhirServer:Security:Authorization:Roles:0:resourcePermissions:0:actions:2": "HardDelete"
}
```

To avoid having to maintain the role definitions in the less readable flattened form, you can use a script to convert the JSON form to the flattened table. There is a an example in the  [ConvertTo-FlattenedConfigurationHashtable.ps1 script](../release/ConvertTo-FlattenedConfigurationHashtable.ps1). To use it:

```PowerShell
$roles = ConvertFrom-Json (Get-Content -Raw .\roles.json)
$flattenedRoles = .\release\scripts\PowerShell\ConvertTo-FlattenedConfigurationHashtable.ps1 -InputObject $roles -PathPrefix "FhirServer:Security:Authorization:Roles"
```

To pass the array of roles in when deploying the FHIR server (see [Deployment Instructions](DefaultDeployment.md) for details):

```PowerShell
$rg = New-AzureRmResourceGroup -Name "RG-NAME" -Location westus2

New-AzureRmResourceGroupDeployment `
-TemplateUri "https://raw.githubusercontent.com/Microsoft/fhir-server/master/samples/templates/default-azuredeploy.json" `
-ResourceGroupName $rg.ResourceGroupName ` 
-serviceName $fhirServiceName ` 
-securityAuthenticationAuthority $apiAppReg.Authority ` 
-securityAuthenticationAudience $apiAppReg.Audience `
-additionalFhirServerConfigProperties $flattenedRoles
```
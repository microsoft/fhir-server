# Microsoft FHIR Server Deployment using Bash

This document describes how to deploy the [Microsoft Open Source FHIR Server](https://github.com/Microsoft/fhir-server). The deployment is a two-step process:

1. (Optional) Create and Azure Active Directory (AAD) Application registration to secure access to the FHIR server. 
2. Deploy Cosmos DB, Azure Web App, and source code using an Azure Resource Manager template. 

## Azure Active Directory Application (AAD) Registration

Azure Active Directory (AAD) application registrations can be created in the Azure portal, but the repository also contains some [bash helper scripts](https://github.com/Microsoft/fhir-server/tree/master/samples/scripts/bash) to assist with the application registration process on the command line:

```bash
#Make sure you are signed in:
az login

#Register the API application registration
apiapp=$(./create-aad-api-application-registration.sh -s msftexamplefhir)


#Register the client application (e.g. for Postman):
clientapp=$(./create-aad-client-application-registration.sh -a $apiappid -d msftexampleclient -r https://www.getpostman.com/oauth2/callback)

#Capture information:
apiappid=$(echo $apiapp | jq -r .AppId)
authenticationAuthority=$(echo $apiapp | jq -r .Authority)
authenticationAudience=$(echo $apiapp | jq -r .Audience)
clientid=$(echo $clientapp | jq -r .AppId )

#Display the client app information:
echo $clientapp
```

The information required to use the application registration will be returned:

```json
{
  "AppId": "c4d0d5f5-XXXX-XXXX-XXXX-2dafe2611c2c",
  "AppSecret": "YjM1MWQwZXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXyYmMzYWVjCg==",
  "ReplyUrl": "https://www.getpostman.com/oauth2/callback",
  "AuthUrl": "https://login.microsoftonline.com/72f988bf-XXXX-XXXX-XXXXX-2d7cd011db47/oauth2/authorize?resource=https://msftexamplefhir.azurewebsites.net",
  "TokenUrl": "https://login.microsoftonline.com/72f988bf-XXXX-XXXX-XXXXX-2d7cd011db47/oauth2/token"
}
```

## Deploying the FHIR Server Template

Use the provided template to deploy the FHIR server:

```bash
#Create resource group
az group create --name msftfhirserver --location westus

#Deploy FHIR server
az group deployment create -g msftfhirserver --template-url https://raw.githubusercontent.com/Microsoft/fhir-server/master/samples/templates/default-azuredeploy.json --parameters serviceName=msftexamplefhir securityAuthenticationAuthority=${authenticationAuthority} securityAuthenticationAudience=${authenticationAudience}
```

Or deploy without OAuth:

```bash
az group deployment create -g msftfhirserver --template-url https://raw.githubusercontent.com/Microsoft/fhir-server/master/samples/templates/default-azuredeploy.json --parameters serviceName=msftexamplefhir
```

You can use [Postman to test the FHIR server](PostmanTesting.md). 

## Clean up Azure AD App Registrations

To remove the AAD application registrations:

```bash
az ad app delete --id $clientid
az ad app delete --id $apiappid
```

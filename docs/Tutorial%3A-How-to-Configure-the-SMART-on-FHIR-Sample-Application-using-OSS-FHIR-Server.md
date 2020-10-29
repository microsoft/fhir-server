You can follow the steps outlined in the document, "[Tutorial: Azure Active Directory SMART on FHIR proxy](https://docs.microsoft.com/en-us/azure/healthcare-apis/use-smart-on-fhir-proxy)." However, there are a few differences, as documented here.

# Enable the SMART on FHIR proxy

Make sure that the values of FhirServer:Security:EnableAadSmartOnFhirProxy and FhirServer:Security:Enabled are set to True.

![image.png](/docs/images/SMARTonFHIR/image-adb646f3-ad32-4ce4-8a73-9376ca82f1a2.png)

The OSS FHIR Server security is handled through the server (or app) using four custom defined [roles](https://github.com/microsoft/fhir-server/blob/master/src/Microsoft.Health.Fhir.Shared.Web/roles.json), globalAdmin, globalWriter, globalReader and globalExporter. It is not done through the portal, and therefore you can leave the App Service Automation in the default setting, "Off".

![image.png](/docs/images/SMARTonFHIR/image-ef9fa38f-a49c-47cf-965f-8a506b0664c1.png)

# Enable CORS

Select "Enable Access-Control-Allow-Credentials" and add your application url. You can use the wildcard "*" instead of a specific url, it is not recommended for security reasons.

![image.png](/docs/images/SMARTonFHIR/image-a8556c35-5754-4ffe-a6d1-07d16fe056f6.png)

# Application Registrations

Please reference the document on "Tutorial: Application Registration for OSS FHIR Server" for more detail.

Update the appsettings.json file and run the app

{
    "FhirServerUrl": "https://xxx.azurehealthcareapis.com",
    "ClientId": "{guid value here}",
    "DefaultSmartAppUrl": "/sampleapp/launch.html"
}

You can now follow the steps in the document for Azure API for FHIR, launch the app and test it.

![image.png](/docs/images/SMARTonFHIR/image-9ee9e40d-fb19-46db-b094-688fafa0fe44.png)

# Troubleshoot the App

If your SMART on FHIR sample does not return data as expected, you can check if 
- the permissions or roles have been granted to the public client app, 
- the SMART on FHIR proxy has been enabled,
- the CORS values have  been configured,
- scopes for the application registration for the server have been defined, and
API permissions have been granted to the public client application.

You can use the free tool Fiddler, filter the network traffic and examine the traffic detail for each request and response. For example, you can specify filters to allow only traffic for the localhost and your OSS FHIR Server, xxx.azurewebsites.net.

![image.png](/docs/images/SMARTonFHIR/image-9e4c121a-c574-4eaf-987f-aefb37cce2b5.png)





 




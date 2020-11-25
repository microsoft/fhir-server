# Azure Active Directory SMART on FHIR proxy

In this tutorial we will cover how to use the proxy to enable SMART on FHIR applications with the FHIR Server. [SMART on FHIR](https://docs.smarthealthit.org/) is a set of open specifications to integrate partner applications with FHIR servers and electronic medical records systems that have FHIR interfaces. 

The majority of the tutorial is available [here](https://docs.microsoft.com/azure/healthcare-apis/use-smart-on-fhir-proxy) but the first two steps need to be completed differently. These steps ensure that your app service settings are configured correctly. 

Navigate to the App Service that was greated as part of the deployment.

## Enable the SMART on FHIR proxy setting
1. Select **Configuration** under Settings
1. Set FhirServer:Security:EnableAadSmartOnFhirProxy and FhirServer:Security:Enabled to **True**.

![App Service Settings](images/SMARTonFHIR/app-service-settings.png)

## Enable CORS

1. Under API select **CORS**
1. Select "Enable Access-Control-Allow-Credentials" 
1. Add your application url.

![CORS](images/SMARTonFHIR/CORS.png)

## Use the proxy to enable SMART on FHIR applications with the FHIR Server
Now that you have ensured that you have right settings, you can follow the steps for the managed service tutorial starting at the [**Configure the reply URL**](https://docs.microsoft.com/azure/healthcare-apis/use-smart-on-fhir-proxy#configure-the-reply-url) step.

## Troubleshoot the App

If your SMART on FHIR sample does not return data as expected, you can check the following:
* the permissions or roles have been granted to the public client app
* the SMART on FHIR proxy has been enabled
* the CORS values has been configured
* the scopes for the application registration for the server have been defined
* API permissions have been granted to the public client application

You can use the free tool [Fiddler](https://www.telerik.com/fiddler) and filter the network traffic and examine the detail for each request and response. For example, you can specify filters to allow only traffic for the localhost and your OSS FHIR Server, xxx.azurewebsites.net.

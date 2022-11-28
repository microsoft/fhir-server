# Troubleshooting

## Application Insights

This sample deploys Application Insights. This will collect information from both Azure API Management and the Azure Functions in this sample. This is the best place to look if you are seeing any HTTP errors at all. This will give you insights into configuration and request information as well as any changes you may need for your own environment.

## Browser Console

For the Authorize Input Application, the browser console is the best place to start. Often you will see configuration issues here because requests are going to a different API than you expect.

## Common Issues

### AAD Issues

This sample makes some assumptions about AAD configuration. You may run into errors if your Azure Active Directory is not setup with the following:

- Allowing users to consent to scopes (this is needed for them to choose and store scopes).
- Allowing developers to register applications.
- Your account needs elevated permissions to setup the context application and 

### Azure Configuration Issues

Some common Azure issues that you need to watch out for are:

- Not enough permissions in Azure
- Resource Providers for applications are not registered. If the deployment fails

### Deployment Issues with the Azure Developer CLI

The Azure Developer CLI creates a deployment in Azure as part of the `azd up` command. To get additional details about deployment issues, it's best to find the newly created resource group in Azure and 

### Unauthorized Errors

Unauthorized errors are generally due to users or backend services not having the proper access setup. Check the following:

- Users
  - You didn't add your user to the SMART Roles group.
- Backend Services
  - You have an invalid JWKS
  - You don't have the correct configuration in KeyVault for your client
  - You didn't give your client the proper role assignments to your FHIR Service.

### Not Found Errors for API Requests

This is usually an issue with Azure API Management. Either an API configuration in Azure API Management is setup correctly or your testing tool is pointed at the wrong API Management Issue.

To find out more, information, check out:

- The request information and trace logs in Azure Application Insights. 
- This could be an issue with the Authorize User Input static app. Check the browser developer tools for more details.
- Ensure your Azure Deployment of the sample was successful. 

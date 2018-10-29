# Azure Active Directory Application Registrations

The Microsoft FHIR Server for Azure uses Azure Active Directory (AAD) for OAuth authentication/authorization. In order to deploy the server and applications interacting with the server, you need to create AAD application registrations and manage application roles that are used for role based acces control.

This document explains how to create these application registrations using the Azure Portal.

## Application Registration for FHIR Server API

Please consult the Azure Active Directory Documentation for details on the steps below:

1. [Register an AAD Application](https://docs.microsoft.com/en-us/azure/active-directory/develop/quickstart-v1-add-azure-ad-app)
2. [Add application roles to the application](https://docs.microsoft.com/en-us/azure/active-directory/develop/howto-add-app-roles-in-azure-ad-apps). You should add at least one role, say `admin`.

Make a note of the application id and/or the identifier URI of the of the API application. This will be used as the `audience` when deploying the FHIR server.  

## Grant User API Roles

Now that you have defined API roles in your application registration, you can [assign those roles to specific users](https://docs.microsoft.com/en-us/azure/active-directory/develop/howto-add-app-roles-in-azure-ad-apps#assign-users-and-groups-to-roles).

## Client Application Registration

For each application that will access the FHIR API, create a client application registration:

1. [Register an AAD Application](https://docs.microsoft.com/en-us/azure/active-directory/develop/quickstart-v1-add-azure-ad-app)
2. [Add Redirect URIs](https://docs.microsoft.com/en-us/azure/active-directory/develop/quickstart-configure-app-access-web-apis#add-redirect-uris-to-your-application) for your application.
3. [Add a client secret](https://docs.microsoft.com/en-us/azure/active-directory/develop/quickstart-configure-app-access-web-apis#add-credentials-to-your-web-application).
4. [Add API Permissions](https://docs.microsoft.com/en-us/azure/active-directory/develop/quickstart-configure-app-access-web-apis#add-permissions-to-access-web-apis). Here you should search for the name of the API application you created above and add any delegated privileges (scopes) that you would like the application to obtain on behalf of the user. If you would like the client application to act as a service client, pick the roles you would like the application to have in the application permission settings. After saving the settings hit the "Grant permissions" button if you have assigned roles to the application (required admin permissions).

Make a note of the client application id, and the client secret. 


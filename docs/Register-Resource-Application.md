# Register a resource application in Azure Active Directory

In this article, you'll learn how to register a resource (or API) application in Azure Active Directory. A resource application is an Azure Active Directory representation of the FHIR server API itself and client applications can request access to the resource when authenticating. The resource application is also known as the *audience* in OAuth parlance. If you are using the open source FHIR Server for Azure, follow the steps below to register a resource application. to learn more about application registration in general, review the Azure API for FHIR documentation on [registering applications](https://docs.microsoft.com/azure/healthcare-apis/fhir-app-registration)

## App registrations in Azure portal

1. In the [Azure portal](https://portal.azure.com), on the left navigation panel, click **Azure Active Directory**.

2. In the **Azure Active Directory** blade click **App registrations**:

    ![Azure portal. New App Registration.](images/resource-application/portal-aad-new-app-registration.png)

3. Click the **New registration**.

## Add a new application registration

Fill in the details for the new application. There are no specific requirements for the display name, but setting it to the URI of the FHIR server makes it easy to find:

![New application registration](images/resource-application/portal-aad-register-new-app-registration-NAME.png)

### Set identifier URI and define scopes

A resource application has an identifier URI (Application ID URI), which clients can use when requesting access to the resource. This value will populate the `aud` claim of the access token. It is recommended that you set this URI to be the URI of your FHIR server. For SMART on FHIR apps, it is assumed that the *audience* is the URI of the FHIR server.

1. Click **Expose an API**

2. Click **Set** next to *Application ID URI*.

3. Enter the identifier URI and click **Save**. A good identifier URI would be the URI of your FHIR server.

4. Click **Add a scope** and add any scopes that you would like to define for your API. You are required to add at least one scope in order to grant permissions to your resource application in the future. If you don't have any specific scopes you want to add, you can add user_impersonation as a scope.

![Audience and scope](images/resource-application/portal-aad-register-new-app-registration-AUD-SCOPE.png)

### Define application roles

The Azure API for FHIR and the OSS FHIR Server for Azure use [Azure Active Directory application roles](https://docs.microsoft.com/azure/architecture/multitenant-identity/app-roles) for role-based access control. To define which roles should be available for your FHIR Server API, open the resource application's [manifest](https://docs.microsoft.com/azure/active-directory/active-directory-application-manifest/):

1. Click **Manifest**:

    ![Application Roles](images/resource-application/portal-aad-register-new-app-registration-APP-ROLES.png)

2. In the `appRoles` property, add the [roles](https://github.com/microsoft/fhir-server/blob/master/src/Microsoft.Health.Fhir.Shared.Web/roles.json):

    ```json
    {
    "$schema": "../Microsoft.Health.Fhir.Core/Features/Security/roles.schema.json",
    "roles": [
        {
            "name": "globalReader",
            "dataActions": [
                "read",
                "resourceValidate"
            ],
            "notDataActions": [],
            "scopes": [
                "/"
            ]
        },
        {
            "name": "globalExporter",
            "dataActions": [
                "read",
                "export"
            ],
            "notDataActions": [],
            "scopes": [
                "/"
            ]
        },
        {
            "name": "globalWriter",
            "dataActions": [
                "*"
            ],
            "notDataActions": [
                "hardDelete"
            ],
            "scopes": [
                "/"
            ]
        },
        {
            "name": "globalAdmin",
            "dataActions": [
                "*"
            ],
            "notDataActions": [],
            "scopes": [
                "/"
            ]
        }
    ]
}
    ```

Client applications are registrations of the clients that will be requesting tokens. There are more details on the managed service documentation about [registering applications](https://docs.microsoft.com/azure/healthcare-apis/fhir-app-registration). In this guide we are going to cover how to registering a confidential client and public client application differs in the open-source FHIR Server compared to the Azure API for FHIR. 

# Register a confidential client application

You can register a confidential client application following most of the steps in the [managed service tutorial](https://docs.microsoft.com/azure/healthcare-apis/register-confidential-azure-ad-client-app). The only difference is that you grant permissions by using the defined app roles that you created when you registers your [resource application](https://github.com/microsoft/fhir-server/blob/master/docs/Register-Resource-Application.md).

1. Select API Permissions from the portal
1. Select My APIs to add permissions to the client app. 
1. Select appropriate role(s) for the application.

![image.png](/docs/images/AppRegOSS/image-4a7f3341-6075-4cd9-935d-96923ad88ecf.png)

1. Similarly, you can assign users and groups to the role(s) using Enterprise Applications from the portal.

![image.png](/docs/images/AppRegOSS/image-8f6232e0-1f75-41b8-aba3-8a392cd52460.png)

# Register a public client application

You can register a public client application as outlined in the document. The only differences are that 
- you grant permissions by using the defined app roles, as described above.
- you specify Redirect URIs for SPA apps, Postman and SMART on FHIR apps using "Mobile and desktop applications", instead of using the "single-page application" platform.

![image.png](/docs/images/AppRegOSS/image-7238b1aa-112b-48da-b6fd-a5e852dca1bf.png)

![image.png](/docs/images/AppRegOSS/image-2c6535a6-9675-4397-a52a-21fce0d844a2.png)

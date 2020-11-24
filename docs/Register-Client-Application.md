Client applications are registrations of the clients that will be requesting tokens. There are more details on the managed service documentation about [registering applications](https://docs.microsoft.com/azure/healthcare-apis/fhir-app-registration). In this guide we are going to cover how to registering a confidential client and public client application differs in the open-source FHIR Server compared to the Azure API for FHIR. 

# Register a confidential client application

You can register a confidential client application following most of the steps in the [managed service tutorial](https://docs.microsoft.com/azure/healthcare-apis/register-confidential-azure-ad-client-app). The only difference is that you grant permissions by using the defined app roles that you created when you registered your [resource application](https://github.com/microsoft/fhir-server/blob/master/docs/Register-Resource-Application.md).

1. Select **API permissions** from the portal
1. Select **Add a permission**
1. Select **My APIs** to add permissions to the client app. 
1. Select appropriate role(s) for the application.

![Add Roles](images/client-application/requestapipermission.png)

# Register a public client application

You can register a public client application as outlined in the [managed service documentation](https://docs.microsoft.com/azure/healthcare-apis/register-public-azure-ad-client-app). The only difference is granting permissions by using the defined app roles, as described above.

The application registration process for OSS FHIR Server is similar to that for Azure API for FHIR, but a few exceptions.

# Register Confidential Application for the OSS FHIR Server

If the value of FhirServer:Security:EnabledOSS for the FHIR server is set to True, you will need to register a confidential application for the server, define app roles, and expose the API to other client applications.

Select Expose an API from the portal, specify your server url in the Application ID Url.
![image.png](/images/AppRegOSS/image-293325d0-d5ba-4e1d-ab49-5595fb0f105f.png)

Then define the user consent and admin consent scope for the FHIR server.
![image.png](/images/AppRegOSS/image-aa69a1a1-acac-40da-beae-22aea328f53f.png)

If you want to pre-authorize an application without requiring user authentication, you can add the application to the list of Authorized client applications. This is an optional step.

Next, select Manifest and define [roles] (https://github.com/microsoft/fhir-server/blob/master/src/Microsoft.Health.Fhir.Shared.Web/roles.json). Make sure that the role values are exactly matched to those defined, and assign a unique GUID value of your choice to each of the roles.

	"appRoles": [
		{
			"allowedMemberTypes": [
				"User",
				"Application"
			],
			"description": "fhir oss admin",
			"displayName": "globalAdmin",
			"id": "ba852bf0-43e3-46f4-88ec-5ce70f5fb6dd",
			"isEnabled": true,
			"lang": null,
			"origin": "Application",
			"value": "globalAdmin"
		},
		{
			"allowedMemberTypes": [
				"User",
				"Application"
			],
			"description": "fhir oss writer",
			"displayName": "globalWriter",
			"id": "07fed378-c437-418a-97ca-8a7962abd6d6",
			"isEnabled": true,
			"lang": null,
			"origin": "Application",
			"value": "globalWriter"
		},
        		{
			"allowedMemberTypes": [
				"User",
				"Application"
			],
			"description": "fhir oss reader",
			"displayName": "globalReader",
			"id": "ed289d3c-3588-4469-914e-79c6cdb0f6e2",
			"isEnabled": true,
			"lang": null,
			"origin": "Application",
			"value": "globalReader"
		},
		{
			"allowedMemberTypes": [
				"User",
				"Application"
			],
			"description": "fhir oss exporter",
			"displayName": "globalWriter",
			"id": "fbf16161-ddf3-42a7-8607-758a3660afe1",
			"isEnabled": true,
			"lang": null,
			"origin": "Application",
			"value": "globalExporter"
		}
	],`
`
When "User" is specified for the allowedMemberTypes property, the defined roles are available to assign to users and groups. When  "Application" is specified for the allowedMemberTypes property, the roles are available to assign to service principals.

# Register Application for a confidential client application

You can register a confidential client application as outlined in the document. The only difference is that you grant permissions by using the defined app roles.

Select API Permissions from the portal, and then My APIs to add permissions to the client app. Select appropriate role(s) for the application.

![image.png](/images/AppRegOSS/image-4a7f3341-6075-4cd9-935d-96923ad88ecf.png)

Similarly, you can assign users and groups to the role(s) using Enterprise Applications from the portal.

![image.png](/images/AppRegOSS/image-8f6232e0-1f75-41b8-aba3-8a392cd52460.png)

# Register Application for a public client application

You can register a public client application as outlined in the document. The only differences are that 
- you grant permissions by using the defined app roles, as described above.
- you specify Redirect URIs for SPA apps, Postman and SMART on FHIR apps using "Mobile and desktop applications", instead of using the "single-page application" platform.

![image.png](/images/AppRegOSS/image-7238b1aa-112b-48da-b6fd-a5e852dca1bf.png)

![image.png](/images/AppRegOSS/image-2c6535a6-9675-4397-a52a-21fce0d844a2.png)







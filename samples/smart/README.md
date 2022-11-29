# Azure ONC (g)(10) SMART on FHIR Sample

This sample demonstrates how Azure Health Data Services can be used to pass the Inferno test for ONC (g)(10) compliance, using Azure Active Directory as the identity provider. Azure Health Data Services is the core of this sample, additionally there is custom code and routing required to fully meet the §170.315(g)(10) Standardized API for patient and population services criteriarequirements. This sample can be used as a reference for building your application and solution to pass Inferno tests for ONC (g)(10).

Note : This sample relies Azure Active Directory as the identity provider. You would need to build a custom solution for identity provider other than Azure Active Directory.

Targeted audience : EHR vendors who are looking to pass ONC (g)(10).

## Components

The following components are deployed with this sample. For more details of how the pieces work together, check out [the technical guide](./docs/technical-guide.md).

- Azure Health Data Services FHIR Service
- Azure API Management
- Azure Active Directory
- Azure Functions
- Azure Storage
- Azure Application Insights

![](./docs/images/overview-architecture.png)

## Sample Deployment

Deployment of this sample requires the creation of supporting Azure services, custom code deployed to Azure Function Apps, and setup in Azure Active Directory. For detailed deployment instructions, check out the [Deployment Document here](./docs/deployment.md).

## Next steps

- Integration of these com
- APIM Developer portal
- Private networking

## Sample Support

If you have questions about this sample, please submit a Github issue. This sample is custom code you must adapt to your own environment and is not supported outside of Github issues.

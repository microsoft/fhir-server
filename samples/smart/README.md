# Azure ONC (g)(10) & SMART on FHIR Sample

This sample demonstrates how Azure Health Data Services can be used to pass the Inferno test for ONC (g)(10) compliance, using Azure Active Directory as the identity provider. While the FHIR Server is the core of this sample, some custom code and routing is required to fully meet the requirements. This sample is therefore *not* using only the FHIR Server but other Azure Services with sample code to pass the Inferno tests. You can use this sample as a starting point for your own application to 

## Deployment

Deployment of this sample requires the creation of supporting Azure services, custom code deployed to Azure Function Apps, and setup in Azure Active Directory. For detailed deployment instructions, check out the [Deployment Document here](./docs/deployment.md).

## Components 

The following components are deployed with this sample. For more details of how the pieces work together, check out [the technical guide](./docs/technical-guide.md).

- FHIR Service
- Azure API Management
- Azure Function
- Azure Storage

![](./docs/overview-architecture.png)

## Status

This sample is still under active development.

### Completed

- Standalone Launch (Confidential Client)
- EHR Launch 
- Standalone Launch Public Client
- Bulk Data APIs
- Auth for SMART Backend Services (RSA384)
- Export storage access

### To Do

- Limited Patient
- Documentation for loading US Core

## Support

For help with this sample, please file a GitHub issue.
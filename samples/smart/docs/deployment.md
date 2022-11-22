# Sample Deployment: Azure ONC (g)(10) & SMART on FHIR Sample

This document will guide you through the steps needed for deploying this sample. Since this sample is a combination of several Azure components and Azure Active Directory configuration, it is not all automated.

## 1. Prerequisites

Before deploying this sample, you will need to install some Azure tools **and** ensure you have the proper access to an Azure subscription / tenant.

- Make sure you have the pre-requisites applications installed on your computer.
  - Azure CLI: Please install this via [the instructions here](https://learn.microsoft.com/cli/azure/install-azure-cli)
  - Azure Developer CLI: Please install this via [the instructions here](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd?tabs=baremetal%2Cwindows)
  - Visual Studio or Visual Studio Code (for debugging sample).
  - NPM (for debugging sample).
- .NET SDK installed (the version specified in [global.json](../../../global.json).)
- Access to an Azure Subscription where you can create resources and add role assignments.
- Elevated access in Azure Active Directory to create Application Registrations and grant Admin Consent.

## 2. Deploy Azure Resources

This sample uses the Azure Developer CLI for deployment. This allows for easy deployment of both infrastructure and code. Deploying this sample requires some light configuration. 

- Open `main.pramaters.json` inside of the `infra` folder and fill out the following parameters:
  - `apimPublisherName`: Sample owner name.
  - `apimPublisherEmail`: Sample owner email address.
  - `contextAadApplicationId`: Leave this blank for now.
- Open a terminal to this directory (`samples/smart`).
- Login with the Azure CLI. Specify the tenant if you have more than one.
  - `az login` or `az login -t <tenant-id>`.
- Run the `azd up` command from this directory. Enter 
  - Environment Name: Prefix for the resource group that will be created to hold all Azure resources ([see more details](https://learn.microsoft.com/azure/developer/azure-developer-cli/faq#what-is-an-environment-name)). You can always create a new environment with `azd env new`.
  - Azure Location: The Azure location where your resources will be deployed.
  - Azure Subscription: The Azure location where your resources will be deployed.
- *NOTE:* This will take about an hour to deploy, mainly for Azure API Management. You can continue with Azure Active Directory setup below.

## 3. Azure Active Directory Setup

It's best practice to register an Application Registration in Azure AD for each client application that will need to access your FHIR Service. This will allow for granular control of data access per application for the tenant administrator and the users.

### Context Application

The Context Application is used to gather user input during the authentication process to correctly handle scopes and other user inputs. The patient picker would need to be implemented in this component per your organization's requirements.

### Patient Standalone Confidential Client Application

The Patient Standalone Launch application is a standard confidential client application which leverages the SMART Scopes exposed by Azure Health Data Services.

- Create a new application in Azure Active Directory. Make sure to select `Web` as the platform and add the redirect URL for Inferno (`https://inferno.healthit.gov/suites/custom/smart/redirect`).
- In API Permissions for this new application, add the below:
  - Azure Healthcare APIs (Delegated)
    - fhirUser
    - launch
    - patient.AllergyIntolerance.read
    - patient.CarePlan.read
    - patient.CareTeam.read
    - patient.Condition.read
    - patient.Device.read
    - patient.DiagnosticReport.read
    - patient.DocumentReference.read
    - patient.Encounter.read
    - patient.Goal.read
    - patient.Immunization.read
    - patient.Location.read
    - patient.MedicationRequest.read
    - patient.Medication.read
    - patient.Observation.read
    - patient.Organization.read
    - patient.Patient.read
    - patient.Practitioner.read
    - patient.PractitionerRole.read
    - patient.Procedure.read
    - patient.Provenance.read
  - Microsoft Graph (Delegated)
    - openid
    - offline_access

- Generate a secret for this application. Save this and the client id for testing Inferno *1. Standalone Patient App*.

![](./images/confidential-client-1.png)

### EHR Launch Confidential Client Application

The EHR Launch application is a standard confidential client application which leverages the SMART Scopes exposed by Azure Health Data Services.

- Create a new application in Azure Active Directory. Make sure to select `Web` as the platform and add the redirect URL for Inferno (`https://inferno.healthit.gov/suites/custom/smart/redirect`).
- In API Permissions for this new application, add the below:
  - Azure Healthcare APIs (Delegated)
    - fhirUser
    - launch
    - patient.AllergyIntolerance.read
    - patient.CarePlan.read
    - patient.CareTeam.read
    - patient.Condition.read
    - patient.Device.read
    - patient.DiagnosticReport.read
    - patient.DocumentReference.read
    - patient.Encounter.read
    - patient.Goal.read
    - patient.Immunization.read
    - patient.Location.read
    - patient.MedicationRequest.read
    - patient.Medication.read
    - patient.Observation.read
    - patient.Organization.read
    - patient.Patient.read
    - patient.Practitioner.read
    - patient.PractitionerRole.read
    - patient.Procedure.read
    - patient.Provenance.read
  - Microsoft Graph (Delegated)
    - openid
    - offline_access

- Generate a secret for this application. Save this and the client id for testing Inferno *3. EHR Practitioner App*.

![](./images/confidential-client-1.png)

### Backend Service Client Application

Azure Active Directory does not support RSA384 and/or ES384 which is required by the SMART on FHIR implementation guide. In order to provide this capability, custom code is required to validate the JWT assertion and return a bearer token generated for the client with the corresponding client secret in an Azure KeyVault.

- Create a new application in Azure Active Directory. No platform or redirect URL is needed.
- Grant this application `FHIR Data Reader` and `FHIR Exporter` role in your FHIR Service. We only support `system/*.read` for now through the `FHIR Data Reader` role.
<!--- In API Permissions for this new application, add the below:
  - Azure Healthcare APIs (Application)
    - system.all.read
- Grant admin consent for your Application on the API Permission page-->
- Generate a secret for this application. Save this and the client id.
- In the resource group that matches your environment, open the KeyVault with the suffix `backkv`.
- Add a new secret that corresponds to the Application you just generated. 
  - Name: Application ID/Client ID of the application
  - Secret: The secret you generated for the application
  - Tags: Make sure to add the tag `jwks_url` with the backend service JWKS URL. For Inferno testing, this is: https://inferno.healthit.gov/suites/custom/g10_certification/.well-known/jwks.json
![](./images/keyvault-reg.png)

- Save the client id for later testing.

## TODO

- Finish context app section.
- Add public client app.
- Talk about Need Patient Banner / SMART Style URL. This needs to be changed in APIM policy.
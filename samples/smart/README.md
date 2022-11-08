# Azure ONC (g)(10) & SMART on FHIR Sample

This sample demonstrates how Azure Health Data Services can be used to pass the Inferno test for ONC (g)(10) compliance, using Azure Active Directory as the identity provider. While the FHIR Server is the core of this sample, some custom code and routing is required to fully meet the requirements. This sample is therefore *not* using only the FHIR Server but other Azure Services to pass the Inferno tests.

## Quickstart

### 1. Pre-requisites
- Make sure you have the prerequisites applications (see below).
  - Azure CLI: Please install this via [the instructions here](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli)
  - Azure Developer CLI: Please install this via [the instructions here](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd?tabs=baremetal%2Cwindows)
- .NET SDK installed (the version specified in [global.json](../../global.json).)
- Access to an Azure Subscription where you can create resources and add role assignments.
- Elevated access in Azure Active Directory to create Application Registrations and grant Admin Consent.

### 2. Deploy the Azure Resources
- Open `main.pramaters.json` inside of the `infra` folder and fill out the following parameters:
  - `apimPublisherName`: Sample owner name.
  - `apimPublisherEmail`: Sample owner email address.
- Open a terminal to this directory (`samples/smart`).
- Login with the Azure CLI. Specify the tenant if you have more than one.
  - `az login` or `az login -t <tenant-id>`.
- Run the `azd up` command from this directory. Enter 
  - Environment Name: Prefix for the resource group that will be created to hold all Azure resources ([see more details](https://learn.microsoft.com/en-us/azure/developer/azure-developer-cli/faq#what-is-an-environment-name)). You can always create a new environment with `azd env new`.
  - Azure Location: The Azure location where your resources will be deployed.
  - Azure Subscription: The Azure location where your resources will be deployed.
- *NOTE:* This will take about an hour to deploy, mainly for Azure API Management. You can continue with Azure Active Directory setup below.

### 3. Setup Azure Active Directory

#### Patient Standalone Confidential Client Application

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

- Generate a secret for this application. Save this and the client id for later testing.

#### EHR Launch Confidential Client Application

- Create a new application in Azure Active Directory. Make sure to select `Web` as the platform and add the redirect URL for Inferno (`https://inferno.healthit.gov/suites/custom/smart/redirect`).
- In API Permissions for this new application, add the below:

  - Azure Healthcare APIs (Delegated)
    - fhirUser
    - launch
    - user.AllergyIntolerance.read
    - user.CarePlan.read
    - user.CareTeam.read
    - user.Condition.read
    - user.Device.read
    - user.DiagnosticReport.read
    - user.DocumentReference.read
    - user.Encounter.read
    - user.Goal.read
    - user.Immunization.read
    - user.Location.read
    - user.MedicationRequest.read
    - user.Medication.read
    - user.Observation.read
    - user.Organization.read
    - user.Patient.read
    - user.Practitioner.read
    - user.PractitionerRole.read
    - user.Procedure.read
    - user.Provenance.read
  - Microsoft Graph (Delegated)
    - openid
    - offline_access

- Generate a secret for this application. Save this and the client id for later testing.

#### SMART FHIRUser Custom Claim

#### Prerequisites

You must have rights to administer claims policy in your Azure Active Directory Tenant and read/write permissions for user profiles in order to proceed.

- Launch Powershell with Administrator privileges
- [Install Azure Active Directory PowerShell for Graph Preview](https://learn.microsoft.com/en-us/powershell/azure/active-directory/install-adv2?view=azureadps-2.0)
- [Install Microsoft Graph PowerShell SDK](https://learn.microsoft.com/en-us/powershell/microsoftgraph/installation?view=graph-powershell-1.0)
- Create the custom claim fhirUser for the OAuth id_token by using the onPremisesExtensionAttribute to store the mapping. This example will use onPremisesExtensionAttribute extensionAttribute1 to store the FHIR resource Id of the user. Run the `Set-AADClaimsPolicy.ps1` script in the [scripts](./scripts) folder.

```powershell
.\Set-AADClaimsPolicy.ps1 -TenantId xxxxxxxx-xxxx-xxxx-xxxx -ExtensionAttributeName extensionAttribute1
```
- In the PowerShell terminal run `Get-AzureADPolicy -All:$true1` to verify that the new claims policy was  created. Copy the Id of the newly created claims policy.

```powershell
Get-AzADServicePrincipal -DisplayName [Name-of-your-app-registration]
```
- Get the `ObjectId` of the enterprise application of your Azure App Registration you created in Section 3 above.

- Associate your Custom Claim with an Azure App Registration using the App Registration Principal Id and Claims Policy Object Id.

```powershell
Add-AzureADServicePrincipalPolicy -Id [Enterprise Application Object Id] -RefObjectId [Claims Policy ObjectId]
```

- Run `Set-FHIRUser.ps1` in the [scripts](./scripts) folder. This script will assign a FHIR patient to an Azure AD User custom claim.

```powershell
.\Set-FHIRUser.ps1 -TenantId [TenantId] -UserObjectId [Azure AD User Object Id] -FHIRId [FHIR Patient Id] -AttributeName [Azure AD extension name]
```

#### Backend Service Client Application

- Create a new application in Azure Active Directory. No platform or redirect URL is needed. 
- In API Permissions for this new application, add the below:
  - Azure Healthcare APIs (Application)
    - system.all.read
- Grant admin consent for your Application on the API Permission page
- Generate a secret for this application. Save this and the client id.
- In the resource group that matches your environment, open the KeyVault with the suffix `backkv`.
- Add a new secret that corresponds to the Application you just generated. 
  - Name: Application ID/Client ID of the application
  - Secret: The secret you generated for the application
  - Tags: Make sure to add the tag `jwks_url` with the backend service JWKS URL. For Inferno testing, this is: https://inferno.healthit.gov/suites/custom/g10_certification/.well-known/jwks.json
![](./docs/keyvault-reg.png)

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

- Connecting sample to FHIR Server (finishing scope work first)
- Limited Patient
- Documentation for loading US Core

## Deploying to a new environment

1. Open a terminal to the same directory as this README.
2. Make sure to login to the correct tenant with the `az cli`. For example: `az login -t 12345678-90ab-cdef-1234-567890abcdef`.
3. Run `azd init` to create a new environment.
  - name: environment name from above
  - location: <your location>
  - subscription: <your-subscription>
4. Edit `infra/main.parameters.json` to add your base Application Registration Application ID as `smartAudience`.
5. Run `azd up` to deploy the Azure infrastructure and function app.

### Working with an active environment

- To change environments, run `azd env select`.
- If you need to change the Azure resources in your environment, change the bicep templates in `/infra` and run `azd provision`.
- To deploy the Function App to Azure, run `azd deploy`.

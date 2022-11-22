# Azure ONC (g)(10) Sample Technical Guide

This document describes how we plan to create a passing ONC (g)(10) sample combining Azure Health Data Services with other Azure services.

## Introduction

ONC (g)(10) is a requirement of EHR vendors laid out by the Office of the National Coordinator. The full name for this requirement is Standardized API for Patient and Population Services criterion § 170.315(g)(10) in the 2015 Edition Cures Update.

## Architecture Overview

To successfully use this ONC (g)(10) sample, your Azure environment must be setup with the below resources.

- Azure Health Data Services with a FHIR Service
- Azure API Management
- Azure Function
  - Needed for certain SMART operations not supported by the FHIR Service or specific to your EHR.
    - Standalone Launch Handler enables the auth flow for standalone launch scenarios.
    - EHR Launch Handler enables the auth flow for EHR launch scenarios.
    - Backend Services Auth Handler enables the auth flow for SMART backend services scenarios.
- Storage Account
  - Needed for Azure Function, assorted static assets, and configuration tables.
- Azure KeyVault
  - Needed for JWKS authentication since Azure AD does not support the RSA384 or ES384 encryption algorithms.
- Azure Static Web Apps
  - Needed for the Patient Standalone authorize flow to properly handle scopes. Azure AD does not support session based scoping. 

![](./images/overview-architecture.png)

## Conformance/Discovery

Conformance for SMART apps allows application developers to target many different EHRs in a unified way. Discovery is the method of which applications know how to interact with a specific EHR. See the [SMART implementation guide](https://www.hl7.org/fhir/smart-app-launch/conformance.html) for more details. There are two different discovery endpoints for SMART on FHIR servers:

- The capability statement (`/metadata`) which holds information about the entire FHIR server.
- The SMART on FHIR Well-Known endpoint (`/.well-known/smart-configuration`) which holds SMART on FHIR specific configuration.

Azure Health Data Services needs some modification to the capability statement and the addition of the SMART on FHIR Well-Known endpoint. We can easily meet both of these with a couple of API Management policy.

- Capability statement policy to transform the conformance statement using a `setBody` policy.
  - Adds our authorize and token Azure Function endpoints so clients know where to authenticate.
  - Change the conformance so signify that we can handle SMART on FHIR clients.
- SMART on FHIR Well-Known policy to return a static string for our SMART on FHIR configuration.

**NOTE:** This will result in a separate conformance statement for clients accessing via API Management versus directly through the FHIR Service since this sample is adding additional capabilities to Azure Health Data Services.

```mermaid
  sequenceDiagram
    participant App
    participant APIM
    participant FHIR
    App ->> APIM: /.well-known/smart-configuration
    APIM ->> App: Static JSON body defined by user
    note over App, APIM: Response is configured in APIM
    App ->> APIM: /metadata
    APIM ->> FHIR: /metadata
    FHIR ->> APIM: Metadata response
    note over APIM: Transformation is configured in APIM
    APIM ->> App: Transformed metadata response
```

## EHR Launch

TODO - confirm this diagram and write informational text (Erik).

```mermaid
  sequenceDiagram
    participant User/App
    participant EHR
    participant APIM
    participant EHR Launch Handler
    participant AAD

    EHR -->> APIM: Send session state to cache
    EHR ->> EHR: User Launches App
    EHR ->> User/App: Launch Request

    User/App ->> APIM: Discovery Request
    APIM -->> User/App: Discovery Repsonse
    User/App ->> APIM:  Authorization Request
    APIM -->> APIM: Cache launch parameter
    APIM ->> EHR Launch Handler: Forward /authorize request    
    EHR Launch Handler ->> User/App: HTTP Redirect Response w/ AAD Transformed /authorize URL
    User/App ->> AAD: /authorize request
    note over EHR Launch Handler, AAD: Transformed to AAD Compatible Request
    AAD -->> User/App: Authorization response (code)
    User/App ->> APIM: /token
    APIM ->> EHR Launch Handler: Forward /token request
    EHR Launch Handler ->> AAD: POST /token on behalf of user
    AAD ->> EHR Launch Handler: Access token response
    note over EHR Launch Handler: Handler will augment the /token response with proper scopes, context
    note over EHR Launch Handler: Handler will NOT create a new token
    EHR Launch Handler ->> APIM: Return token response
    APIM -->> APIM: Pull cached launch parameter, decode
    APIM ->> User/App: Forward token response
    User/App ->> FHIR: Request Resources
```

## Standalone Launch

SMART standalone launch refers to when an app launches from outside an EHR session. Generally these are patient facing apps since patients often do not access an EHR to view their data (patients could have EHR launch apps from an EHR patient portal). This flow often relies on user input for establishing the correct context for the SMART application since there is no session data to pull from. There are two potential scenarios where an EHR application may need to gather input from the user:

- Selecting a record (like a patient) when a user has access to mulitple patients (like parent/child relatonship)
- Reviewing and granting limited scopes to an application to control how much of the patient's data the SMART app can access

*I think (need to verify)* ONC (g)(10) does not require a patient picker, so it is out of scope for this sample. If we need it, it's not too bad.

Azure Active Directory does not have a mechanism for selecting a subset of scopes when approving/denying an application. Due to this, we have to serve a custom scope selection interface for standalone launch scenarios.

```mermaid
  sequenceDiagram
    participant User/App
    participant APIM
    participant SMART Auth Custom Operations
    participant AAD
    participant FHIR
    participant Graph
    User/App ->> APIM: Discovery Request
    APIM ->> User/App: Discovery Repsonse
    User/App ->> APIM: /authorize
    alt Scope Selector
        note over User/App: Session scope selection
        APIM ->> User/App: Scope Selector static webpage
        opt Scope Update
            User/App ->> APIM: User Selected Scopes
            APIM ->> SMART Auth Custom Operations:  Forward User Selected Scopes
            SMART Auth Custom Operations ->> Graph: Save user scope preferences for app
        end
        
        User/App ->> APIM: Redirect: /authorize        
    end

    APIM ->> SMART Auth Custom Operations: Forward /authorize request    
    SMART Auth Custom Operations ->> AAD: /authorize
    note over SMART Auth Custom Operations, AAD: Limited scopes, transformed
    AAD ->> User/App: Authorization response (code)
    User/App ->> APIM: /token
    APIM ->> SMART Auth Custom Operations: Forward /token request
    SMART Auth Custom Operations ->> AAD: POST /token on behalf of user
    AAD ->> SMART Auth Custom Operations: Access token response
    note over SMART Auth Custom Operations: Handler will augment the /token response with proper scopes, context
    note over SMART Auth Custom Operations: Handler will NOT create a new token
    SMART Auth Custom Operations ->> APIM: Return token response
    APIM ->> User/App: Forward token response
```

## Backend Service Authorization

Backend Service Authorization is part of the [FHIR Bulk Data Access Implementation Guide](https://hl7.org/fhir/uv/bulkdata/STU1.0.1/authorization/index.html). Backend services are intended to be used by developers of backend services (clients) that autonomously (or semi-autonomously) need to access resources from FHIR servers that have pre-authorized defined scopes of access. It is a combination of a client registration process (using JSON Web Keys), token generation without the sharing of secrets, and using SMART on FHIR with `system` scopes to access data.

SMART Backend Services requires that FHIR servers allow client asymmetric authentication with RSA384 and/or ES384. Active Directory does not support this natively today, so the SMART Auth Custom Operations has code to handle these backend authorization request. This code in the SMART auth handlers function is responsible for validating the backend service authentication request, creating an Azure Active Directory token using the matching secret in Azure KeyVault, and returning the token to the backend service for use when calling Azure Health Data Services.

### Backend Service Registration

Client registration is an out-of-band process required before backend services can access data from the FHIR server. Client registration can be an automated or manual process. In our sample, we have a manual client registration process that must be done during backend service registration. The process is as follows:

- Collect the JWKS url from the client who needs to register.
- Create an Application Registration for the backend service. Generate a client secret and save both.
- In the Azure KeyVault created when deploying the sample, you need to create a new secret where the name is the client_id and the value is the secret.
  - You also must create a tag on the secret called `jwks_url` containing the url for the backend service. The SMART Auth Custom Operation for backend services will use this tag to validate the backend service.  

![](./images/keyvault-reg.png)

### Backend Service Authorization Flow

```mermaid
  sequenceDiagram
    participant Backend Service
    participant APIM
    participant SMART Auth Custom Operations
    participant KeyVault
    participant FHIR

    Backend Service ->> APIM: Discovery Request
    APIM -->> Backend Service: Discovery Response
    Backend Service ->> Backend Service: Generate RSA 384 Client Assertion
    Backend Service ->> APIM: /token
    APIM ->> SMART Auth Custom Operations: /token request
    SMART Auth Custom Operations ->> KeyVault: client_id
    KeyVault -->> SMART Auth Custom Operations: jwks_url & secret

    note over SMART Auth Custom Operations: Validate assertion
    alt Granted
        note over SMART Auth Custom Operations: Generate AAD token using secret
        SMART Auth Custom Operations -->> APIM: Token Response
        APIM -->> Backend Service: Access Token Response
        Backend Service ->> APIM: Request Resources
        APIM ->> FHIR: Request Resources
        FHIR -->> Backend Service: FHIR Response
    else Denied
        SMART Auth Custom Operations -->> APIM: Authorization Error
        APIM -->> Backend Service: Authorization Error
    end
```

## Resources

- [SMART on FHIR 1.0 Implementation Guide](https://hl7.org/fhir/smart-app-launch/1.0.0/)
- [FHIR Bulk Data Access Implementation Guide (STU1.0.1)](https://hl7.org/fhir/uv/bulkdata/STU1.0.1/)
- [ONC Health IT Certification Program API Resource Guide](https://onc-healthit.github.io/api-resource-guide/g10-criterion/)
- [Inferno Testing Tool](https://inferno.healthit.gov/)
- [ONC Certification (g)(10) Standardized API Test Kit on Github](https://github.com/onc-healthit/onc-certification-g10-test-kit)

# TODO

- EHR Launch
- Rewriting of resource URLs
- Missing Data
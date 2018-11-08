# Features
The Microsoft FHIR Server for Azure is an implementation of the [FHIR](https://hl7.org/fhir) standard. This document list the main features of the FHIR server. For a list of features that planned or in development, see the [feature roadmap](Roadmap.md).

## FHIR Version
Current version: `3.0.1`

## Persistence
The Microsoft FHIR server has a pluggable persistence module (see [`Microsoft.Health.Fhir.Core.Features.Persistence`](../src/Microsoft.Health.Fhir.Core/Features/Persistence)). 

Currently the FHIR Server source code includes an implementation for [Azure Cosmos DB](https://docs.microsoft.com/en-us/azure/cosmos-db/). 

Cosmos DB is a globally distributed non-  supports different [consistency levels](https://docs.microsoft.com/en-us/azure/cosmos-db/consistency-levels). The default deployment template configures the FHIR Server with `Strong` consistency, but the consistency policy can be modified (generally relaxed) on a request by request basis using the `x-ms-consistency-level` request header.

## Search

## Role Based Access Control 
The FHIR Server uses Azure Active Directory for access control. Specifically if the `FhirServer:Security:Enabled` configuration parameter is set to `true`, all requests (except `/metadata`) to the FHIR server must have `Authorization` request header set to `Bearer <TOKEN>`. The token must contain one or more role defined in the `roles` claim. A request will be allowed if the token contains a role that allows the specified action on the specified resource. 

Currently, allowed actions for a specific role can only be specified *globally* on the API. [Future work](Roadmap.md) is aimed at allowing more granular access based on a set of policies and filters.  

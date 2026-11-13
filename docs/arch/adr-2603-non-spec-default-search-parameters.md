# Non-Spec Default Search Parameters

## Context 
   A need has arisen to define default search parameters for our FHIR Server that are not explicitly defined in the FHIR specification. These parameters will enhance the search capabilities of our server and provide additional functionality that is tailored to our specific use cases.

## Decision
   A new JSON file holding the non-spec default search parameters will be added. This file will have the same structure as the spec-defined search parameter file and will be loaded with the same mechanism.

## Status
   Accepeted

## Consequences
   Benefits:
   - Allows us to define custom search parameters that are not part of the FHIR specification, providing greater flexibility and functionality for our users.
   Drawbacks:
   - Has potential conflicts with customer created search parameters. To mitigate this we will use a naming convention that reduces the likelihood of conflicts.
   - Need to handle adding new parameters to existing servers.

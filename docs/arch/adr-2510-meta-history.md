# Meta History

## Context 
   Per the [FHIR spec](https://build.fhir.org/resource-definitions.html#Resource.meta) when metadata is updated on a FHIR resource a historical version doesn't need to be created. For customers who make many changes to resource metadata having undesired versions in the database bloats data size, leading to extra expense and reduced performance.

## Decision
   To facilitate this need a new query string parameter will be added to PUT requests, `_meta-history`. This new parameter will prevent a historical version from being created if the only fields changed are in the meta block. It will still add a historical version if there are changes outside of meta.

   It was decided to make this a query string parameter as this gives it the most flexibility within our service. Query string parameters can be used in bundles on individual requests, while headers on bundles are all or nothing.
   
   This is planned to be added to PATCH and bulk update operations in the future.

## Status
   Proposed

## Consequences
   This introduces a way to make changes to a resource without creating a historical version. This will create a situation where there isn't a perfect historical version trail for resource changes. As this is allowed by the FHIR spec and is an optional flag this was decided to be an acceptable risk. 


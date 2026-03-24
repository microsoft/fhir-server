# ADR 2603: Search Parameters Concurrency
*Labels*: [SQL](https://github.com/microsoft/fhir-server/labels/Area-SQL) | [Core](https://github.com/microsoft/fhir-server/labels/Area-Core) | [SearchParameter](https://github.com/microsoft/fhir-server/labels/Area-SearchParameter)

## Context

### Problem
The FHIR server is not protected from race conditions when multiple concurrent requests attempt creating or updating the search params with conflicting information simultaneously.

### Root Cause
As a part of create/update workflow search param is validated. Both search param internal integrity and absence of conflicts with the database (reference set) are checked. Reference set validation runs against search param cache, so cache refresh happens first. If validation is successful, search param is created/updated in the database. Each validation against reference set is performed with assumption of isolation, i.e. there are no conflicts across all racing search params.
In previous implementation https://github.com/microsoft/fhir-server/blob/main/docs/arch/adr-2512-searchparameter-concurrency-management.md an assumption was made that conflicts are caused by processing of search params with same Uris only. In reality, there are other conflicts, more difficult to detect, such as conflicts on Codes and/or their search path expressions. Unlike Uris, uniqueness on these data points cannot be easiliy expressed as the database constraints.

## Decision

We will implement database-level optimistic concurrency across all search parameters based on max(LastUpdated) in the SearchParam table.
This implementation superceeds previous implementation https://github.com/microsoft/fhir-server/blob/main/docs/arch/adr-2512-searchparameter-concurrency-management.md that was based on individual search params, and was not able to gurantee cross search param consistency.

### Database-Level Optimistic Concurrency  
Before validation, we will read the current value for max(LastUpdated) from the SearchParam table (across all search params). When we attempt to create/update search param(s), we will check that the max(LastUpdated) value has not changed since we read it. If it has changed, it means another concurrent operation has modified the reference set, and our create/update will fail with a concurrency conflict error. 

## Status
**Accepted**

## Consequences
Data correctness will be enforced by ensuring that concurrent modifications to search parameters are properly serialized, preventing bad data ingested into the database.

## Notes
This ADR superceeds previous implementation https://github.com/microsoft/fhir-server/blob/main/docs/arch/adr-2512-searchparameter-concurrency-management.md.
Previous implementation should to be removed. 

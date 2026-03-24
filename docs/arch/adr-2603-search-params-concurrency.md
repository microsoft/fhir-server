# ADR 2603: Search Parameters Concurrency
*Labels*: [SQL](https://github.com/microsoft/fhir-server/labels/Area-SQL) | [Core](https://github.com/microsoft/fhir-server/labels/Area-Core) | [SearchParameter](https://github.com/microsoft/fhir-server/labels/Area-SearchParameter)

## Context
### Problem
The FHIR server is not protected from race conditions when multiple concurrent requests attempt create or update the search params with conflicting data.

### Root Cause
As a part of create/update workflow search param is validated. Both search param internal integrity and absence of conflicts with the reference set are checked. Reference set validation runs against search param cache, so cache refresh must happen first. If validation is successful, search param is created/updated in the database. Each validation against reference set is performed with assumption of isolation, i.e. there are no conflicts across all racing search params.
Previous implementation https://github.com/microsoft/fhir-server/blob/main/docs/arch/adr-2512-searchparameter-concurrency-management.md makes an assumption that conflicts are caused by racing of search params with same Uris only. In reality, there are other logical constraints based on Codes and their search expressions. Unlike Uris, uniqueness on these data points cannot be easiliy expressed and requires validation across all search params, and also requires reference set stability during both validation and write.

## Decision
We will implement optimistic concurrency across all search params based on max(LastUpdated) in the SearchParam table.

### Optimistic Concurrency  
Before validation, we will read the current value for max(LastUpdated) from the SearchParam table (across all search params). When we attempt to create/update search param(s), we will check that the max(LastUpdated) value has not changed since we read it. If it has changed, it means another concurrent operation has modified the reference set, and our create/update will fail with a concurrency conflict error. 

## Status
**Accepted**

## Benefits
- Data correctness will be enforced by ensuring that concurrent modifications to search param set are properly serialized, preventing conflicting data ingested into the database.
- Code symplicity will be improved by removing the need for complex locking and retry logic based on individual search params.

## Notes
This ADR superceeds previous implementation https://github.com/microsoft/fhir-server/blob/main/docs/arch/adr-2512-searchparameter-concurrency-management.md.
Previous implementation should to be removed. 

# ADR: Conditional Updates with If-Match header
Labels: [Specification](https://github.com/microsoft/fhir-server/labels/Specification) | [API](https://github.com/microsoft/fhir-server/labels/Area-API)

## Context
Currently conditional updates using the `If-Match` header with an ETag does not consider an etag that might be passed by the client. Specifically, the server ignores the `If-Match` header and performs updates even when the provided ETag does not match the current version of the resource.

## Decision
We will implement a fix to ensure that the `If-Match` header is correctly validated during conditional updates. The server will:

1. Parse and validate the `If-Match` header against the current version of the resource (if found).
2. Reject requests with a `412 - Precondition Failed` status if the ETag does not match the current version.
3. Log detailed information for debugging purposes when a mismatch occurs.

## Considerations for Conditional Update States

When implementing the fix for conditional updates with optimistic locking, the following states must be handled appropriately:

1. **No Match:**
   - If no resource matches the search criteria, a create should be performed as before.
   
   - **Question: Which should be true?**
     - The `If-Match` header is irrelevant in this case as no resource exists to validate against.
     - OR a conflict is returned since a record was expected to be updated
       - Using an etag with a special value of <=0 would specify a create must be performed and would fail if a record is found

2. **Single Match:**
   - If exactly one resource matches the search criteria, the server should validate the `If-Match` header against the current version of the resource.
   - If the ETag matches, the update proceeds, and the resource version is incremented.
   - If the ETag does not match, the server should return a `412 - Precondition Failed` status.

3. **Multiple Matches:**
   - If multiple resources match the search criteria, the server should return a `400 - Bad Request` status.
   - Conditional updates are only valid when a single resource matches the search criteria.

These considerations ensure compliance with FHIR standards and provide predictable behavior for clients using conditional updates.

## Status
Proposed

## Consequences
- **Positive:**
  - Ensures compliance with FHIR standards for conditional updates.
  - Improves reliability and predictability of optimistic locking mechanisms.
  - Enhances user trust in the server's behavior for critical operations.
- **Negative:**
  - Requires thorough testing to ensure no regressions in related functionality.
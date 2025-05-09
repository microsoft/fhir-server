# ADR: Conditional Updates with If-Match and If-None-Match header interactions
Labels: [Specification](https://github.com/microsoft/fhir-server/labels/Specification) | [API](https://github.com/microsoft/fhir-server/labels/Area-API)

## Context
Currently, conditional updates using the `If-Match` header with an ETag do not consider an ETag that might be passed by the client. Specifically, the server ignores the `If-Match` header and performs updates even when the provided ETag does not match the current version of the resource. Additionally, the server does not currently support the `If-None-Match: *` header on PUT to force a create operation. To align with FHIR standards and provide predictable, standards-compliant behavior.

## Decision
We will implement a fix to ensure that the `If-Match` and `If-None-Match` headers are correctly validated during conditional updates. The server will:

1. Parse and validate the `If-Match` header against the current version of the resource (if found).
2. Support the `If-None-Match: *` header on PUT to force a create operation. If a resource already exists that matches the search criteria, the server will return a `412 - Precondition Failed` status.
3. If both `If-Match` and `If-None-Match` headers are provided in the same request, the server will return a `400 - Bad Request` status.
4. Only `If-None-Match: *` is supported for forced create. If `If-None-Match` is used with a specific ETag value (e.g., `If-None-Match: W/"1"`) for create with PUT, the server will return an Operation Not Supported exception.
5. Reject requests with a `412 - Precondition Failed` status if the ETag does not match the current version (for `If-Match`).
6. Log detailed information for debugging purposes when a mismatch or invalid combination occurs.

## Considerations for Conditional Update States

When implementing the fix for conditional updates with optimistic locking and forced create, the following states must be handled appropriately:

1. **No Match:**
   - If no resource matches the search criteria:
     - If `If-None-Match: *` is present, the resource will be created by PUT.
     - If `If-Match` is present, the header is irrelevant as no resource exists to validate against, but the operation will fail since there's no resource to match the ETag.
     - If both headers are present, return `400 - Bad Request`.

2. **Single Match:**
   - If exactly one resource matches the search criteria:
     - If `If-Match` is present, the server should validate the header against the current version of the resource.
       - If the ETag matches, the update proceeds, and the resource version is incremented.
       - If the ETag does not match, the server should return a `412 - Precondition Failed` status.
     - If `If-None-Match: *` is present, the server should return a `412 - Precondition Failed` status (resource already exists, cannot create).
     - If both headers are present, return `400 - Bad Request`.

3. **Multiple Matches:**
   - If multiple resources match the search criteria, the server should return a `400 - Bad Request` status.
   - Conditional updates are only valid when a single resource matches the search criteria.

4. **Resource ID Considerations:**
   - When a client provides a resource ID in the resource being updated:
     - If no match is found: The provided ID is used for the new resource.
     - If a single match is found and the provided ID matches the found resource ID: The update proceeds normally.
     - If a single match is found but the provided ID differs from the found resource ID: The server returns a `400 - Bad Request` status.
   - These resource ID rules apply regardless of whether conditional headers are present.

These considerations ensure compliance with FHIR standards and provide predictable behavior for clients using conditional updates and forced creates.

## Status
Proposed

## Consequences
- **Positive:**
  - Ensures compliance with FHIR standards for conditional updates and forced creates.
  - Improves reliability and predictability of optimistic locking mechanisms.
- **Negative:**
  - Requires thorough testing to ensure no regressions in related functionality.
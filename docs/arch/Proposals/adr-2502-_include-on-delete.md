# ADR: Support for `_include` Parameter in Delete Requests
Labels: [Specification](https://github.com/microsoft/fhir-server/labels/Specification) | [API](https://github.com/microsoft/fhir-server/labels/Area-API)

## Context

In the FHIR specification, the `_include` parameter allows clients to request related resources to be returned alongside the primary resource in search operations. However, this functionality was not previously extended to delete operations within the FHIR Server. This limitation meant that when a resource was deleted, any resources that referenced or were associated with it remained unaffected, potentially leading to orphaned or inconsistent data. To enhance data integrity and provide more comprehensive delete operations, there was a need to implement support for the `_include` parameter in delete requests, specifically for **bulk-delete** operations and **Delete with `_count`**.

## Decision

**Conditional-delete restriction:** The `_count` portion of this proposal is superseded. Conditional DELETE rejects `_include` and `_revinclude`, including all modifiers, with HTTP 400 before searching or deleting any resources. This applies to soft and hard deletion, single and multiple deletion counts, and conditional DELETE entries in batch and transaction bundles. Included search results are not deletion matches, and search permission on a related resource does not grant delete permission. See the FHIR [conditional delete](https://hl7.org/fhir/R4/http.html#cdelete) and [search inclusion](https://hl7.org/fhir/R4/search.html#include) specifications.

The inclusion behavior below applies only to the separately authorized bulk-delete operation. Its existing restriction on fine-grained SMART callers remains unchanged; normal search inclusion behavior is also unchanged.

We will implement support for the `_include` parameter in delete requests. This enhancement will allow clients to specify related resources that should also be deleted when a primary resource is deleted. The implementation will involve updating the delete operation logic to process the `_include` parameter, identify related resources based on the inclusion criteria, and delete them alongside the primary resource. This approach ensures that related resources are appropriately managed during delete operations, maintaining data consistency and integrity.

The changes will apply to:
- **Bulk-delete operations**: Enabling `_include` for bulk deletions ensures that all related resources are removed efficiently in large-scale delete operations.
- **Delete with `_count`**: Inclusion parameters are rejected, regardless of the requested count. Clients must issue separately authorized delete requests for related resources.

## Status

Proposed

## Consequences

By supporting the `_include` parameter in delete requests, the FHIR Server will provide more robust data management capabilities. Clients will have the ability to perform cascading deletions, ensuring that related resources are removed alongside primary resources when appropriate. This enhancement reduces the risk of orphaned data and maintains referential integrity within the FHIR data store. However, clients must use this feature judiciously to avoid unintended data loss, and thorough testing will be necessary to ensure that the implementation handles various scenarios correctly.

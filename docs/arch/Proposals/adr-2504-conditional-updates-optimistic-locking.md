# ADR 2504: Conditional Updates with Optimistic Locking

## Context
Conditional Updates are a FHIR feature that allows updating resources based on search criteria rather than direct resource IDs. The FHIR server currently implements this functionality as specified by the FHIR standard (https://www.hl7.org/fhir/http.html#cond-update). 

Optimistic locking is implemented via ETag/If-Match headers to ensure that updates are only applied if the resource has not been modified since it was last retrieved. This is critical for maintaining data integrity in concurrent environments.

The current implementation has an issue (#4647) where conditional updates do not properly work with optimistic locking. When a client performs a conditional update with an `If-Match` header (containing an ETag), the optimistic locking check is not correctly applied. This can lead to race conditions and data integrity issues in scenarios where multiple clients attempt to update the same resource.

The issue affects three major conditional update scenarios:
1. **Zero matches**: When no resources match the search criteria
2. **Single match**: When exactly one resource matches the search criteria
3. **Multiple matches**: When more than one resource matches the search criteria

## Decision
We will update the conditional update implementation to properly handle optimistic locking across all three scenarios:

1. For **zero matches** (no resource found):
   - If an If-Match header is provided, we'll reject the request with a 412 Precondition Failed error, as there is no resource to match against the provided ETag.
   - If no If-Match header is provided, we'll follow the existing behavior: create a new resource if the client provided an ID, or generate a new ID and create a resource if no ID was provided.

2. For **single match** (one resource found):
   - If an If-Match header is provided, we'll verify the ETag matches the version of the found resource. If it doesn't match, we'll return a 412 Precondition Failed error.
   - If the ETag matches, we'll proceed with the update as normal.
   - If no If-Match header is provided, we'll continue with the existing behavior of updating the matched resource.

3. For **multiple matches** (multiple resources found):
   - Always return a 412 Precondition Failed error as per FHIR specification, regardless of whether an If-Match header is provided.

The primary changes will be in the `ConditionalUpsertResourceHandler` class which handles conditional update operations. We'll modify the `HandleSingleMatch` and `HandleNoMatch` methods to incorporate ETag validation when an If-Match header is present.

## Status
Proposed

## Consequences

### Positive Outcomes
- Improved data integrity through proper optimistic locking during conditional updates
- Elimination of potential race conditions where concurrent conditional updates could result in data loss

### Potential Challenges
- Additional complexity in the conditional update logic to properly handle ETags
- Need for additional testing to ensure all scenarios are properly covered

### Testing Approach
- Add comprehensive unit tests for the `ConditionalUpsertResourceHandler` class with specific scenarios for each case:
  - Zero matches with/without ETag
  - Single match with correct ETag
  - Single match with incorrect ETag
  - Single match without ETag
  - Multiple matches with/without ETag
- Add E2E tests that verify the behavior across all three data stores (SQL Server, Cosmos DB, etc.)
- Add specific tests for edge cases around version handling

### Future Considerations
- Consider adding clearer documentation around conditional updates and optimistic locking
- Monitor performance impact of additional validation checks
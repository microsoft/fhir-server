# If-Match Consistency Design

## Context

The FHIR service currently handles `If-Match` inconsistently across operations that resolve to one resource. Regular update and patch requests can carry the header to persistence, but conditional update replaces the client value with the search result version, conditional patch drops the value before persistence, and delete requests do not carry it at all. These gaps bypass the configured `versioned-update` policy and can allow a stale client to modify or delete a newer resource version.

FHIR uses `If-Match: W/"<version>"` for optimistic concurrency. A supplied version that does not match the current resource version must fail with `412 Precondition Failed`. When a resource type advertises `versioned-update`, an existing resource cannot be updated without a valid `If-Match` header. The service preserves its established missing-header response behavior: STU3 returns 412, while later FHIR versions return 400.

## Scope

Apply consistent `If-Match` behavior to requests that target or resolve to one resource:

- regular update and patch, preserving their existing behavior;
- conditional update when the search resolves to one existing resource;
- conditional JSON Patch and conditional FHIRPath Patch when the search resolves to one resource;
- regular soft delete, hard delete, and purge-history;
- conditional soft or hard delete in single-delete mode when the search resolves to one resource; and
- equivalent interactions executed as bundle entries.

Conditional operations that create after no match do not require `If-Match`. Multi-resource conditional delete does not apply a single ETag to multiple resources and remains outside this behavior.

## Design

### Header propagation

The API controller binds `If-Match` with the existing `WeakETagBinder` for conditional update, regular delete, and conditional delete, matching regular update and patch. Request message types carry the parsed `WeakETag` through MediatR. Bundle processing already maps a bundle entry's `request.ifMatch` into the inner HTTP request, so it follows the same controller path without separate concurrency logic.

Handlers must preserve the client-provided ETag. They must not synthesize a tag from a conditional search result or omit the tag when creating the final persistence request.

### Conditional request validation

When a conditional update, patch, or single delete finds one resource, a supplied ETag is compared with the matched resource version before applying a payload or starting deletion. A mismatch returns `412 Precondition Failed` and does not invoke persistence.

The client ETag is still forwarded to persistence after this comparison. This keeps persistence authoritative and protects against a concurrent write between conditional search and mutation.

### Versioned-update enforcement

Existing resource upserts continue to obtain the resource type's policy through `IConformanceProvider.RequireETag`. The final upsert operation receives both the client ETag and `requireETagOnUpdate`, allowing SQL Server and Cosmos DB to atomically reject:

- a missing ETag when the resource type uses `versioned-update`; and
- an ETag whose version is no longer current.

Delete operations use the same policy. Soft delete supplies the ETag and policy to the tombstone upsert. Hard delete and purge-history validate the ETag against the current resource before destructive persistence because their datastore API does not accept an ETag. This check covers the requested behavior but cannot make the existing hard-delete API's read-and-delete sequence atomic; changing that datastore contract is outside this focused fix.

### Error behavior

- Malformed `If-Match` values continue to be rejected by `WeakETagBinder` as bad requests.
- A supplied stale ETag returns 412.
- A missing ETag for an existing `versioned-update` resource preserves current service compatibility: 412 for STU3 and 400 for later versions.
- Conditional no-match, multiple-match, and create behavior retains its existing FHIR-specific status codes.
- No mutation occurs after an ETag mismatch.

## Testing

Focused unit tests cover request propagation, single-match comparisons, missing-header policy forwarding, and suppression of persistence after mismatches.

End-to-end tests use a resource type configured as `versioned-update` and cover:

- regular update;
- regular soft delete, hard delete, and purge-history;
- conditional update;
- conditional single delete;
- conditional JSON Patch; and
- conditional FHIRPath Patch.

For each applicable interaction, tests prove that a current ETag succeeds, a stale ETag returns 412 without mutation, and an omitted ETag is rejected for an existing resource. Conditional create/no-match coverage confirms that `versioned-update` does not require an ETag when no existing resource is modified. Existing bundle coverage is extended where needed to prove that `Bundle.entry.request.ifMatch` reaches the same behavior.

Configuration-specific E2E assertions run against the in-process server, where the test resource versioning policy is controlled. Backend coverage includes SQL Server and Cosmos DB, and shared tests continue to compile for STU3, R4, R4B, and R5.

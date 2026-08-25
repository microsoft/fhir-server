# If-Match Race Fix Report

Date: 2026-08-24
Commit: `de7e8ba88` (`Restore conditional If-Match CAS`)

## Scope

This fix resolves the two branch-review findings recorded in `task-6-report.md`:

1. A conditional update without a client `If-Match` had lost its search-to-write compare-and-swap guard.
2. A single-match conditional delete with Include results dropped its client `If-Match` before the Match reached persistence.

## Design and implementation

### Conditional update

`WeakETag` remains exclusively the client-provided header value. A separate optional
`ComparedVersion` string now carries the version observed by the conditional search:

`ConditionalUpsertResourceHandler` -> `UpsertResourceRequest` ->
`ResourceWrapperOperation` -> `DataStoreOperationIdentifier` -> data store.

The conditional-update handler always preserves `request.WeakETag` unchanged and sets
`ComparedVersion` from the searched Match. Consequently:

- a versioned-update resource still receives a null client ETag when its header is
  omitted, so existing STU3 412 and later-version 400 missing-header policy enforcement
  remains authoritative;
- an ordinary versioned/no-version conditional update retains an internal expected
  version at persistence; and
- conditional create/no-match continues to carry no compared version and can create
  without `If-Match`.

`DataStoreOperationIdentifier` includes `ComparedVersion` in equality and hash code
calculation so result correlation remains correct.

SQL Server rejects a missing/stale `ComparedVersion` before generating a new version
and treats a SQL conflict on a compared operation as a precondition failure. Cosmos
skips its optimistic-create shortcut when a compared version exists, rejects a missing
or mismatched current resource, and retains Cosmos native `_etag` CAS for the final
write. Neither backend synthesizes a client `WeakETag`.

### Conditional delete with Includes

The conditional-delete handler now clones the request and marks the specific
single-Match-with-Includes route. `DeletionService` uses that marker only when its
page contains exactly one Match:

- The Match soft-delete operation receives the client `WeakETag` and
  `RequireETagOnUpdate`; Include operations receive neither.
- Merge outcome failures for the guarded Match are rethrown as their original FHIR
  exception rather than silently reported as a successful bulk delete.
- Guarded soft and hard deletes process and validate the Match before Includes, so a
  stale/missing-header failure leaves neither target nor Include mutated.
- SearchParameter soft deletes bypass `ResourceWrapperOperation`; their guarded route
  now performs the same current-version precheck before changing SearchParameter state.
- Multi-match requests retain existing behavior: a single ETag is not copied to
  multiple Match operations.

Hard delete still uses the approved non-atomic `GetAsync`/validate/`HardDeleteAsync`
sequence because the datastore hard-delete contract has no version parameter. This
contract was not changed.

## Strict TDD evidence

### RED

1. Command:

   ```powershell
   dotnet test src/Microsoft.Health.Fhir.R4.Core.UnitTests/Microsoft.Health.Fhir.R4.Core.UnitTests.csproj --no-restore --filter "FullyQualifiedName~ResourceHandlerTests|FullyQualifiedName~DeletionServiceTests|FullyQualifiedName~DataStoreOperationIdentifierTests"
   ```

   Result: failed as expected (exit code 1): `DataStoreOperationIdentifier` had no
   `comparedVersion` constructor parameter.

2. Command:

   ```powershell
   dotnet test src/Microsoft.Health.Fhir.R4.Core.UnitTests/Microsoft.Health.Fhir.R4.Core.UnitTests.csproj --no-restore --filter "FullyQualifiedName~DeletionServiceTests"
   ```

   Result: failed as expected (2 of 19 failed): stale guarded soft and hard conditional
   deletes had already mutated their Include before rejecting the Match.

3. Command:

   ```powershell
   dotnet test src/Microsoft.Health.Fhir.R4.Core.UnitTests/Microsoft.Health.Fhir.R4.Core.UnitTests.csproj --no-restore --filter "FullyQualifiedName~DeletionServiceTests"
   ```

   Result: failed as expected (1 of 20 failed): a stale guarded SearchParameter soft
   delete completed without a `PreconditionFailedException`.

### GREEN

The final focused R4 command was:

```powershell
dotnet test src/Microsoft.Health.Fhir.R4.Core.UnitTests/Microsoft.Health.Fhir.R4.Core.UnitTests.csproj --no-restore --filter "FullyQualifiedName~ResourceHandlerTests|FullyQualifiedName~DeletionServiceTests|FullyQualifiedName~DataStoreOperationIdentifierTests"
```

Result: 113 passed, 0 failed, 0 skipped.

The guarded-delete-only GREEN command was:

```powershell
dotnet test src/Microsoft.Health.Fhir.R4.Core.UnitTests/Microsoft.Health.Fhir.R4.Core.UnitTests.csproj --no-restore --filter "FullyQualifiedName~DeletionServiceTests"
```

Result: 20 passed, 0 failed, 0 skipped.

## Final verification

| Command scope | Result |
| --- | --- |
| R4 focused shared-core tests | 113 passed |
| R4B focused shared-core tests | 113 passed |
| R5 focused shared-core tests | 113 passed |
| STU3 focused shared-core tests | 113 passed |
| Cosmos DB unit tests | 103 passed |
| SQL Server unit tests | 1,055 passed |

The focused shared-core suite includes metadata propagation, versioned-update policy
forwarding, Match-versus-Include routing, stale soft/hard delete behavior,
SearchParameter routing, and multi-Match ETag non-propagation. SQL unit coverage also
includes an ambient transaction conflict using `ComparedVersion`.

An attempted R4 in-process E2E run for the existing conditional versioned-update
missing-header/no-match-create tests was blocked before compilation because that
project enforces exact SDK `10.0.302`, while the environment has `10.0.303`.

## Review

Focused code-quality, CAS-contract, and silent-failure reviews were run after the
implementation. The silent-failure review identified Include-before-precondition and
SearchParameter special-path gaps; both were fixed with new RED/GREEN tests. The final
review found no high-confidence remaining defects.

## Remaining limitation

Hard delete retains the approved non-atomic read-before-delete limitation because
`IFhirDataStore.HardDeleteAsync` does not accept a version/ETag precondition. No
datastore API change was made.

## Next Fix Round — Authoritative No-Op CAS and Guarded Delete Completion

### Scope

This round resolves the follow-up review findings against `de7e8ba88`:

1. guarded SQL and Cosmos logical no-ops now settle their comparison at an
   authoritative datastore boundary;
2. a guarded target that has disappeared fails its precondition consistently;
3. the one guarded conditional-delete Match carries `ComparedVersion` without
   propagating it or the client ETag to Includes or multiple Matches; and
4. SQL translates a merge conflict to 412 only after correlating the actual
   resource whose comparison failed.

### Files

Production changes:

- `src/Microsoft.Health.Fhir.Core/Messages/Delete/DeleteResourceRequest.cs`
- `src/Microsoft.Health.Fhir.Core/Resources.resx`
- `src/Microsoft.Health.Fhir.Core/Resources.Designer.cs`
- `src/Microsoft.Health.Fhir.Shared.Core/Features/Resources/Delete/ConditionalDeleteResourceHandler.cs`
- `src/Microsoft.Health.Fhir.Shared.Core/Features/Resources/Delete/DeletionService.cs`
- `src/Microsoft.Health.Fhir.CosmosDb/Features/Search/Queries/QueryBuilder.cs`
- `src/Microsoft.Health.Fhir.CosmosDb/Features/Storage/CosmosFhirDataStore.cs`
- `src/Microsoft.Health.Fhir.CosmosDb/Features/Storage/FhirCosmosClientInitializer.cs`
- `src/Microsoft.Health.Fhir.CosmosDb/Features/Storage/GuardConfirmationResult.cs`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Storage/SqlServerFhirDataStore.cs`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Storage/SqlStoreClient.cs`

Regression coverage was added or extended in the SQL, Cosmos, and shared-core
unit-test projects, including a production-serializer Cosmos container
simulation and Cosmos `IdAndType` projection coverage.

### Design and implementation

#### SQL Server

- Guarded no-op updates and delete-of-tombstone operations perform a primary
  `UPDLOCK` current-version probe before returning success. The probe excludes
  `HOLDLOCK`: the target was already matched as an existing row, so a
  serializable key-range lock is unnecessary and the `MergeResources.sql`
  precedent documents the paired hint as deadlock-prone.
- The guarded snapshot is never read from a replica. A disappeared guarded
  target yields a per-operation `PreconditionFailedException`; an unguarded
  missing delete remains an idempotent no-op.
- `WeakETag` numeric normalization is used consistently by the snapshot,
  no-op probe, and conflict-correlation paths. For example, `W/"01"` matches
  stored version `1` without changing the client-visible error value.
- Generic SQL merge conflicts are correlated by re-reading only guarded
  resources and identifying an actual version mismatch. If correlation cannot
  prove one, processing falls back to the generic conflict path rather than
  fabricating a 412 for an unrelated bundle entry.

#### Cosmos DB

- Guarded logical no-ops use an ETag-conditional server-side
  `PatchItemAsync` that pins `/version` to its current reported value. This
  provides an authoritative CAS without reserializing a read
  `FhirCosmosResourceWrapper`; reserializing that wrapper would corrupt its
  write-only search-index representation.
- The patch creates no FHIR version or history record. For an older document
  whose FHIR version was derived from `_etag`, it materializes that same value
  as an explicit `version` property so the reported version remains stable
  across the physical `_etag` change.
- The no-op confirmation loop is bounded. A true stale/missing or
  unconfirmable guard fails closed with 412. Repeated physical contention
  between same-version no-ops is reported as a retryable 409 rather than a
  false stale-version 412.
- The Cosmos `IdAndType` projection now includes both `version` and `_etag`,
  preserving the version observed by a conditional search with Includes.

#### Guarded conditional delete

- `DeleteResourceRequest` carries the internal `ComparedVersion` only for the
  direct single-Match path. Both direct and Include-bearing routes fail closed
  if a search cannot provide the Match version, before persistence,
  SearchParameter mutation, reference removal, or Include mutation.
- On the Include route, only the one Match receives `WeakETag`,
  `RequireETagOnUpdate`, and `ComparedVersion`; Includes and multi-Match
  operations receive none of them.
- The Match is validated/persisted before Includes. SearchParameter handling
  validates its guarded Match before changing status or touching Includes.
- A missing target with client `If-Match` or internal `ComparedVersion` now
  returns 412 for soft delete, hard delete, and purge-history. A regular
  missing delete with neither guard remains idempotent.

### Strict TDD evidence

All race tests are deterministic seams or in-memory simulations; none use
sleep, wall-clock timing, or a live concurrency race.

#### RED

| Command | Expected RED result |
| --- | --- |
| `dotnet test src\Microsoft.Health.Fhir.CosmosDb.UnitTests\Microsoft.Health.Fhir.CosmosDb.UnitTests.csproj -- --filter "FullyQualifiedName~CosmosFhirDataStoreGuardedNoOpTests\|FullyQualifiedName~QueryBuilderProjectionTests\|FullyQualifiedName~FhirCosmosResourceWrapperSerializationTests"` | 10 total, 5 failed before the patch/projection implementation: read-wrapper replacement stripped search indices and `IdAndType` lost the observed version. |
| `dotnet test src\Microsoft.Health.Fhir.CosmosDb.UnitTests\Microsoft.Health.Fhir.CosmosDb.UnitTests.csproj --filter "FullyQualifiedName~CosmosFhirDataStoreGuardedNoOpTests" --no-restore` | 12 total, 5 failed before the fail-closed/capped confirmation implementation: missing ETag accepted success and same-version contention was unbounded. |
| `dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --filter "FullyQualifiedName~SqlServerFhirDataStoreUnitTests"` | 30 total, 6 failed before the authoritative no-op probe and correlated-conflict implementation. |
| `dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --filter "FullyQualifiedName~MergeAsync_GivenGuardedNoOpUpdate_WhenWeakETagHasNonCanonicalNumericFormat"` | Failed before normalization: `W/"01"` incorrectly failed an otherwise matching no-op comparison. |
| `dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --filter "FullyQualifiedName~MergeAsync_OnSqlConflictInMultiResourceBatch_WhenGuardedOperationHasNonCanonicalNumericWeakETagThatStillMatches"` | Failed before correlation normalization: unrelated conflict was falsely translated to 412. |
| `dotnet test src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj --filter "FullyQualifiedName~DeletionServiceTests"` | 36 total, 8 failed before missing-target guard and unavailable-Match-version protection. |
| `dotnet test src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj --filter "FullyQualifiedName~GivenOneMatchingResourceWithUnavailableVersionAndNoClientWeakETag"` | Failed before the direct Match-only fail-closed guard because persistence was invoked with no usable `ComparedVersion`. |

#### GREEN

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore
# 1068 passed, 0 failed, 0 skipped

dotnet test src\Microsoft.Health.Fhir.CosmosDb.UnitTests\Microsoft.Health.Fhir.CosmosDb.UnitTests.csproj --no-restore
# 127 passed, 0 failed, 0 skipped

dotnet test src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj --no-restore
# 1785 passed, 0 failed, 1 skipped (version-specific pre-existing skip)

dotnet test src\Microsoft.Health.Fhir.R4B.Core.UnitTests\Microsoft.Health.Fhir.R4B.Core.UnitTests.csproj --no-restore --filter "FullyQualifiedName~Delete"
# 79 passed, 0 failed, 0 skipped

dotnet test src\Microsoft.Health.Fhir.R5.Core.UnitTests\Microsoft.Health.Fhir.R5.Core.UnitTests.csproj --no-restore --filter "FullyQualifiedName~Delete"
# 79 passed, 0 failed, 0 skipped

dotnet test src\Microsoft.Health.Fhir.Stu3.Core.UnitTests\Microsoft.Health.Fhir.Stu3.Core.UnitTests.csproj --no-restore --filter "FullyQualifiedName~Delete"
# 79 passed, 0 failed, 0 skipped

git diff --check
# clean
```

### Self-review

- Ran repeated code-quality, silent-failure, type-design, and comment-accuracy
  reviews. Findings on Cosmos serializer safety, Cosmos include projection,
  missing guarded targets, SQL no-op locking, SQL conflict attribution,
  numeric ETag normalization, and contention classification were fixed with
  new RED/GREEN tests.
- The final code review found no high-confidence correctness issue in the
  authoritative no-op paths, Match/Include propagation, or SQL correlation.
- The final diff passed `git diff --check`.

### Remaining limitations

- `IFhirDataStore.HardDeleteAsync` remains unchanged. Hard delete and
  purge-history can only read/validate before destructive persistence; a write
  landing after that read is outside the approved datastore contract. The
  implementation guarantees no Include mutation once a stale/disappeared
  guarded Match is detected.
- SearchParameter deletion similarly lacks a version-bearing datastore
  operation. It is guarded by the strongest available read/validate boundary
  before status or Include mutation, but cannot be made fully atomic without a
  datastore contract change.
- Cosmos guarded no-ops incur one conditional patch/RU and may materialize an
  explicit version field for an ETag-versioned legacy document. No FHIR
  version/history is created. Cosmos coverage uses the production serializer
  and deterministic container simulation; it was not run against a live
  emulator/service in this environment.

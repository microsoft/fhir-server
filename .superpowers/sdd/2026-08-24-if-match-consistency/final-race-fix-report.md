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

## Fix Round 4 - Guarded No-Op Lock Ordering, Merge Transaction Settlement, Contention Mapping

### Scope

This round resolves the three review findings against `a163533cc`:

1. the guarded no-op probe took its `UPDLOCK` on `IX_Resource_ResourceTypeId_ResourceId`
   - the opposite lock order from every writer - and its SQL deadlock/lock-timeout
   errors (1205/1222) were neither bounded nor mapped, so they could surface as 500;
2. the new SQL statements had no live-backend execution evidence; and
3. a guarded probe failure after `MergeResourcesBeginTransactionAsync` escaped without
   settling the merge transaction, leaving an orphan for the transaction watchdog.

### Files

Production:

- `src/Microsoft.Health.Fhir.SqlServer/Features/Storage/SqlStoreClient.cs`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Storage/SqlServerFhirDataStore.cs`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Storage/MergeTransactionSettlement.cs` (new)

Tests:

- `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Storage/SqlServerFhirDataStoreUnitTests.cs`
- `test/Microsoft.Health.Fhir.Shared.Tests.Integration/Persistence/SqlServerGuardedNoOpTests.cs` (new)
- `test/Microsoft.Health.Fhir.Shared.Tests.Integration/Microsoft.Health.Fhir.Shared.Tests.Integration.projitems`

No Cosmos production code was changed, no schema/stored procedure was added or
altered, and `IFhirDataStore.HardDeleteAsync` is untouched.

### Design and implementation

#### Lock ordering (finding 1)

The probe still settles the comparison authoritatively under an update lock on the
primary - the lock was not removed and no pre-read/replica comparison was
reintroduced - but it now claims that lock where the store's writers already claim
theirs:

- the row is located through `IX_Resource_ResourceTypeId_ResourceId` **without** a
  lock, and its surrogate id is collected into a table variable;
- the lock is then taken on the clustered key `(ResourceTypeId, ResourceSurrogateId)`
  with `ROWLOCK, UPDLOCK, INDEX = PKC_Resource`, filtered on `IsHistory = 0`.

`dbo.MergeResources` locks that same clustered key in its retry check
(`ROWLOCK, HOLDLOCK` on the surrogate id join) and again when it stamps the previous
version as history, before nonclustered index maintenance. Locking the clustered key
therefore follows the established order instead of inverting it; the previous form
took the index lock first and the clustered lock later (via the enclosing bundle),
which is the inversion the review flagged and the same pairing `MergeResources.sql`
rejects on its own version comparison join. `HOLDLOCK` is still not used.

The `INDEX = PKC_Resource` hint is load bearing rather than cosmetic: every column the
statement reads is also carried by `IX_Resource_ResourceTypeId_ResourceId`, so without
it the optimizer covers the read from that nonclustered index and the update lock lands
on exactly the index row the change is trying not to lock first. This was caught by the
live integration tests, not by inspection (see RED #3 below).

Correctness is unchanged by the two-step form: if the current row is replaced between
the two statements, the old surrogate id is no longer current, is not returned, and the
caller reports the established guarded-disappearance 412.

#### Bounded wait and error mapping (finding 1)

`SET LOCK_TIMEOUT 10000` bounds the probe's wait and is restored to the SQL Server
default (`-1`) at the end of the batch, because an enlisted probe runs on the bundle's
shared connection. Losing the wait is classified rather than propagated:

- 1205 (deadlock victim) and 1222 (lock request timeout) map to
  `TransactionDeadlockException` (`Core.Resources.TransactionDeadlock`), which the
  existing `OperationOutcomeExceptionFilterAttribute` already maps to 409;
- not 412: a lost lock says nothing about whether the caller's version is stale, so no
  precondition failure is fabricated, and no unrelated bundle conflict is reclassified -
  the existing conflict-correlation path is untouched;
- when the merge is enlisted in a sequential transaction bundle the exception is thrown
  (that transaction is unusable), matching the established fail-fast handling of an
  ambient SQL conflict; otherwise it is recorded as that one operation's outcome, so the
  rest of a batch is unaffected and `UpsertAsync` still surfaces it as a 409.

#### Merge transaction settlement (finding 3)

`MergeTransactionSettlement` is an `IAsyncDisposable` that settles the transaction id
handed out by `dbo.MergeResourcesBeginTransaction` unless the merge reached the point
where `dbo.MergeResources` and its existing failure handling own it
(`TransferToMergeExecution`). It covers a throwing probe, a cancellation, and the
bundle-transaction early return that previously leaked one open transaction per failed
entry. Settlement is best effort and logged, so it cannot replace the failure that
ended the merge, and the deliberate "leave it for the watchdog to roll forward" path for
non single-transaction merges is preserved.

### Strict TDD evidence

#### RED

1. Command:

   ```powershell
   dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --filter "FullyQualifiedName~SqlServerFhirDataStoreUnitTests"
   ```

   Result: failed as expected - 40 total, 6 failed. The lock-surface test found no
   clustered-key lock, both contention cases produced a raw SQL failure instead of a
   mapped outcome, and both settlement tests showed `dbo.MergeResourcesBeginTransaction`
   executed with no matching `dbo.MergeResourcesCommitTransaction`
   (`Collection: ["dbo.MergeResourcesBeginTransaction"]`).

2. Command (live SQL Server):

   ```powershell
   dotnet test test\Microsoft.Health.Fhir.R4.Tests.Integration\Microsoft.Health.Fhir.R4.Tests.Integration.csproj --filter "FullyQualifiedName~SqlServerGuardedNoOpTests"
   ```

   Result: failed as expected - 5 total, 1 failed:
   `GivenASequentialTransactionBundle_WhenAGuardedOperationFailsItsPrecondition_ThenNoMergeTransactionIsLeftOpen`
   reported `Expected: 0 / Actual: 1` open rows in `dbo.Transactions`. This is finding 3
   reproduced against a real database.

3. Command (live SQL Server, after the first cut of the lock change):

   ```powershell
   dotnet test test\Microsoft.Health.Fhir.R4.Tests.Integration\Microsoft.Health.Fhir.R4.Tests.Integration.csproj --filter "FullyQualifiedName~SqlServerGuardedNoOpTests"
   ```

   Result: failed as expected - 6 total, 2 failed. With `ROWLOCK, UPDLOCK` but no index
   hint, the enlisted probe held no lock on the resource row after the merge, and a
   writer holding that row did not block the probe at all. That is what forced the
   explicit `INDEX = PKC_Resource` hint.

#### GREEN

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --filter "FullyQualifiedName~SqlServerFhirDataStoreUnitTests"
# 39 passed, 0 failed, 0 skipped

dotnet test test\Microsoft.Health.Fhir.R4.Tests.Integration\Microsoft.Health.Fhir.R4.Tests.Integration.csproj --filter "FullyQualifiedName~SqlServerGuardedNoOpTests"
# 6 passed, 0 failed, 0 skipped (live SQL Server)
```

### SQL integration evidence

`SqlServerGuardedNoOpTests` runs against a real SQL Server database created by
`SqlServerFhirStorageTestsFixture` (local default instance, SQL Server 2025 Developer
Edition, schema `SchemaVersionConstants.Max`). It executes the guarded statement itself,
not a stub:

| Test | What it proves on a live database |
| --- | --- |
| `GivenAGuardedNoOpUpdate_WhenTheStoredVersionStillMatches_ThenItIsHonoredWithoutCreatingANewVersion` | The two-statement guarded probe parses and runs; a matching guarded no-op succeeds and `dbo.Resource` still holds exactly one row at version 1 (no version, no history). |
| `GivenAGuardedNoOpUpdate_WhenTheStoredVersionMovedOn_ThenItFailsItsPreconditionWithoutMutatingTheResource` | A stale guard returns `PreconditionFailedException` and leaves the stored rows exactly as they were. |
| `GivenTheAuthoritativeProbe_WhenAConcurrentWriterHoldsTheRow_ThenItWaitsForThatWriterAndObservesTheCommittedVersion` | With an uncommitted writer holding the row, the probe is still waiting after 2s, and after that writer commits it returns the new version - it is serialized with the writer rather than reading a pre-write state. |
| `GivenASequentialTransactionBundle_WhenAGuardedNoOpIsHonored_ThenNoVersionIsCreatedAndNoMergeTransactionIsLeftOpen` | The enlisted probe runs on the bundle's transaction, holds an update lock on the resource's clustered row for the life of the bundle (a `LOCK_TIMEOUT 0` clustered read fails with 1222 while it is open and succeeds after commit), creates no version, and leaves no open merge transaction. |
| `GivenASequentialTransactionBundle_WhenTheGuardedProbeCannotObtainItsLock_ThenItFailsAsRetryableContentionAndLeavesNoOpenMergeTransaction` | A real, unresolvable lock wait inside a bundle produces SQL 1222, which surfaces as `TransactionDeadlockException` (409) instead of a `SqlException`, with no orphan merge transaction and no mutation. This exercises the enlisted mapping end to end. |
| `GivenASequentialTransactionBundle_WhenAGuardedOperationFailsItsPrecondition_ThenNoMergeTransactionIsLeftOpen` | The finding 3 leak: `dbo.Transactions` gains no uncompleted row when a bundle transaction returns early with a failed precondition. |

### Full verification

| Command | Result |
| --- | --- |
| `dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj` | 1,073 passed, 0 failed |
| `dotnet test src\Microsoft.Health.Fhir.CosmosDb.UnitTests\Microsoft.Health.Fhir.CosmosDb.UnitTests.csproj` | 127 passed, 0 failed |
| `dotnet test src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj` | 1,785 passed, 0 failed, 1 skipped (pre-existing version-specific skip) |
| `dotnet test test\...R4.Tests.Integration... --filter "FullyQualifiedName~SqlServerGuardedNoOpTests\|FullyQualifiedName~SqlServerSetMergeTests\|FullyQualifiedName~SqlServerTransactionScopeTests\|FullyQualifiedName~SqlServerWatchdogTests"` | 20 passed, 0 failed (live SQL) |
| `dotnet test test\...R4.Tests.Integration... --filter "FullyQualifiedName~FhirStorageTests&FullyQualifiedName~SqlServer"` | 45 passed, 4 failed, 2 skipped - identical on a stashed clean tree (see below) |
| `git diff --check` | clean |

Two SQL-backed results needed a baseline comparison, and both were reproduced exactly
with this round's changes stashed (`git stash push -u`), so neither is caused by it:

- `FhirStorageTests(SqlServer)`: the same 4 `UpdateSearchParameterIndicesAsync` /
  `BulkUpdateSearchParameterIndicesAsync` tests fail before and after (51 total,
  4 failed, 45 succeeded, 2 skipped in both runs).
- The unfiltered `--filter "FullyQualifiedName~SqlServer"` run aborts the test host with
  `Error output: Process terminated.` and `error: 1` while reporting 0 test failures.
  The captured stack is a background history-search `BundleFactory.CreateBundle` call,
  unrelated to persistence writes, and it reproduces identically on the clean tree
  (there: 95 total, 0 failed, 2 skipped). SQL-backed classes were therefore run in
  batches, as listed above.

### Self-review

- The lock-order claim is not left as an assertion about SQL text alone: the live tests
  verify that the probe blocks behind an uncommitted writer, that it holds a lock on the
  clustered row for the life of an enlisted bundle, and that the lock is released with
  that bundle. The first implementation of this round passed a text-level review and
  still failed both live assertions, which is exactly why the hint is now explicit.
- Contention mapping deliberately reuses `TransactionDeadlockException`
  ("There was resource contention with another process in the datastore. Please retry
  this transaction.") for both 1205 and 1222 rather than inventing a resource string;
  both are contention, both are retryable, and both are already 409.
- `TryGetLockContentionError` unwraps a wrapping `AggregateException` the same way
  `MergeAsync` already does, so classification is consistent with the surrounding code.
- Settlement is scoped by an explicit transfer point rather than a blanket catch, so the
  existing, deliberate decision to leave a non single-transaction merge open for the
  transaction watchdog is preserved; the watchdog integration tests were run and pass.
- Visible no-op semantics are unchanged: a matching guarded no-op still creates no FHIR
  version and no history row, verified against a live database rather than a mock.

### Remaining limitations

- The 10s probe lock timeout is a fixed constant rather than configuration. Under
  sustained contention on one resource a guarded no-op can return 409 where an unbounded
  wait would eventually have returned 200; that trade is deliberate, since the enlisted
  form holds a bundle transaction open while it waits.
- A deadlock (1205) on the guarded probe is mapped and tested by injection at the unit
  level; the live integration test induces the bounded lock timeout (1222), because
  provoking a genuine deadlock cycle deterministically would require ordering two
  in-flight merges against each other.
- Cosmos DB logic is unchanged this round and still has no live emulator/service
  execution; its coverage remains the production serializer plus deterministic container
  simulation.
- `IFhirDataStore.HardDeleteAsync` is unchanged, so hard delete and purge-history keep
  the previously approved read/validate-before-delete limitation.
- `FhirStorageTests(SqlServer)` has 4 pre-existing failures in this environment, and the
  broad `~SqlServer` integration filter aborts its test host; both reproduce on an
  unmodified tree and are out of scope for this round.

## Fix Round 5 - Bounded Merge Transaction Settlement

### Scope

This round resolves the one remaining Important finding against `db52323ed`:

`MergeTransactionSettlement.DisposeAsync` settled with `CancellationToken.None` through the
normal SQL retry service. On a datastore blip that attempt could spend the configured retry
count multiplied by the configured (deliberately large) command timeout, blocking an
already-failing request for minutes, even though the transaction watchdog is the durable
backstop for exactly this transaction.

### Files

Production:

- `src/Microsoft.Health.Fhir.SqlServer/Features/Storage/MergeTransactionSettlement.cs`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Storage/SqlStoreClient.cs`

Tests:

- `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Storage/MergeTransactionSettlementTests.cs` (new)
- `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Storage/SqlServerFhirDataStoreUnitTests.cs`

No SQL statement, schema, stored procedure, Cosmos file, or `IFhirDataStore` contract was
changed. `SqlServerFhirDataStore` is untouched: the settlement boundary and the
`TransferToMergeExecution` transfer point are exactly as Round 4 left them.

### Design and implementation

The cleanup attempt is now bounded on three independent axes, and none of them is the
caller's token:

- **Its own token.** `DisposeAsync` creates `new CancellationTokenSource(SettlementTimeout)`
  and settles under that token. It is deliberately *not* linked to the caller's token: the
  most common way to reach disposal without transferring is that the caller's token was
  already cancelled, and linking would remove the one in-band chance to clean up in exactly
  the case that most needs it. The requirement "must not use the caller's already-cancelled
  token as its sole chance" is met by not using it at all.
- **No retries.** The call passes `disableRetries: true`, so `SqlRetryService.ExecuteSql`
  rethrows on the first failure (`SqlRetryService.cs` line 271) instead of looping
  `MaxRetries` times with `RetryMillisecondsDelay` between attempts.
- **A pinned command timeout.** `SqlStoreClient.MergeResourcesCommitTransactionAsync` gained
  two optional parameters (`commandTimeoutSeconds`, `disableRetries`). Settlement passes
  `ceil(SettlementTimeout)` seconds. This matters because `SqlRetryService.ExecuteSql`
  replaces the command timeout with the large store-wide value *only* when it still holds the
  `SqlCommand` default of 30 (`SqlRetryService.cs` line 257); any other explicit value
  survives to the wire. `SettlementTimeout` is asserted to stay in `[1s, 10s]`, so it can
  never collide with that 30-second sentinel.

`SettlementTimeout` is 5 seconds. That is the extra latency an already-failing request is
allowed to cost: long enough for a healthy or briefly-loaded store to settle the single-row
update in band, far below the retry-times-command-timeout budget that produced the finding.

Everything the finding required to be preserved is preserved:

- Every pre-`dbo.MergeResources` exit still attempts settlement. The only skip is the
  unchanged `_transferred` guard.
- `catch (Exception e)` still wraps the whole attempt, so a timeout
  (`OperationCanceledException`/`TaskCanceledException`) and a store failure are both
  swallowed for the caller and neither can replace the exception that ended the merge.
- Both outcomes are logged through one `LogWarning` call carrying `{TransactionId}`,
  `{SettlementTimeoutMs}` and an `{SettlementOutcome}` of `abandoned` or `failed`, so an
  operator can tell "the bound fired" from "the store refused" without two log shapes.
- Successful cleanup still runs on its own connection through `ISqlRetryService`
  (`isReadOnly: false`), and `dbo.MergeResourcesCommitTransaction` is unchanged and still
  idempotent via its `@IsCompletedBefore` check, so a late-landing settlement is harmless.
- The two new `SqlStoreClient` parameters are optional; the other
  `MergeResourcesCommitTransactionAsync` call sites (`SqlServerFhirDataStore.cs` lines 567
  and 577, and the import/watchdog paths) keep the caller's token and the full retry budget.

### Strict TDD evidence

All new tests are deterministic. The unresponsive-settlement fakes answer *only* when their
own token is cancelled, so those tests can complete only if the bound actually fires; no
assertion compares a wall-clock duration and nothing sleeps to create a race. The 30-second
`Task.WhenAny` guards exist solely to turn "never returned" into a failed assertion instead
of a hung test host, and are two orders of magnitude above the 250 ms bound they observe.

#### RED

1. Command:

   ```powershell
   dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --filter "FullyQualifiedName~MergeTransactionSettlementTests|FullyQualifiedName~SqlServerFhirDataStoreUnitTests"
   ```

   Result: failed as expected - `Build failed with exit code: 1`. The settlement type exposed
   no bound at all:

   ```text
   MergeTransactionSettlementTests.cs(68,84): error CS0117: 'MergeTransactionSettlement' does not contain a definition for 'SettlementTimeout'
   MergeTransactionSettlementTests.cs(96,55): error CS0117: 'MergeTransactionSettlement' does not contain a definition for 'SettlementTimeout'
   MergeTransactionSettlementTests.cs(107,34): error CS1729: 'MergeTransactionSettlement' does not contain a constructor that takes 4 arguments
   MergeTransactionSettlementTests.cs(129,34): error CS1729: 'MergeTransactionSettlement' does not contain a constructor that takes 4 arguments
   ```

2. The bound was then introduced as API surface only - `SettlementTimeout` plus the
   bound-injecting constructor - with `DisposeAsync` still settling under
   `CancellationToken.None` through the ordinary retry path, so the behavioral gap could be
   observed as assertions rather than as a compile error. Command:

   ```powershell
   dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --filter "FullyQualifiedName~MergeTransactionSettlementTests|FullyQualifiedName~SqlServerFhirDataStoreUnitTests"
   ```

   Result: failed as expected - `total: 49, failed: 6, succeeded: 43`:

   | Failing test | RED message |
   | --- | --- |
   | `MergeTransactionSettlementTests.DisposeAsync_WhenTheMergeNeverReachedMergeResources_ThenSettlementIsBoundedAndDoesNotRetry` | `Settlement must not re-enter the SQL retry loop while the caller waits.` |
   | `MergeTransactionSettlementTests.DisposeAsync_WhenTheMergeNeverReachedMergeResources_ThenSettlementGetsItsOwnCancellableToken` | `Settlement must run under an explicitly bounded token.` |
   | `MergeTransactionSettlementTests.DisposeAsync_WhenSettlementNeverResponds_ThenItGivesUpWithinItsBoundInsteadOfBlockingTheCaller` | `Settlement did not give up: an unresponsive datastore blocked disposal.` (30s 209ms) |
   | `MergeTransactionSettlementTests.DisposeAsync_WhenSettlementIsAbandoned_ThenTheWarningIdentifiesTheTransaction` | `Settlement did not give up: an unresponsive datastore blocked disposal.` (30s 016ms) |
   | `SqlServerFhirDataStoreUnitTests.MergeAsync_GivenSettlementThatNeverResponds_ThenTheOriginalMergeFailureStillPropagates` | `MergeAsync stayed blocked on an unresponsive settlement instead of surfacing its own failure.` (30s 373ms) |
   | `SqlServerFhirDataStoreUnitTests.MergeAsync_GivenCallerCancellationBeforeMergeResources_ThenSettlementIsStillAttemptedWithAnUncancelledToken` | `Settlement must not re-enter the SQL retry loop while the caller waits.` |

   Two of the new tests passed in RED and are recorded as regression locks rather than
   reproductions: `MergeAsync_GivenSettlementThatItselfFails_ThenTheOriginalMergeFailureIsWhatPropagates`
   (Round 4's blanket catch already preserved the original exception - the defect was the
   blocking, not a swallow) and
   `DisposeAsync_AfterTransferToMergeExecution_ThenTheTransactionIsLeftToMergeResources`
   (the watchdog roll-forward boundary must not regress).
   `MergeAsync_GivenCallerCancellationBeforeMergeResources_...` additionally locks the
   half of the contract a naive fix would break: it fails if settlement is ever linked to the
   caller's cancelled token.

#### GREEN

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --filter "FullyQualifiedName~MergeTransactionSettlementTests|FullyQualifiedName~SqlServerFhirDataStoreUnitTests"
# Test run summary: Passed!  total: 49, failed: 0, succeeded: 49, skipped: 0
```

### Full verification

| Command | Result |
| --- | --- |
| `dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --filter "FullyQualifiedName~MergeTransactionSettlementTests\|FullyQualifiedName~SqlServerFhirDataStoreUnitTests"` | 49 passed, 0 failed, 0 skipped |
| `dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj` | 1,083 passed, 0 failed, 0 skipped |
| `dotnet test test\Microsoft.Health.Fhir.R4.Tests.Integration\Microsoft.Health.Fhir.R4.Tests.Integration.csproj --filter "FullyQualifiedName~SqlServerGuardedNoOpTests"` | 6 passed, 0 failed, 0 skipped (live SQL Server) |
| `dotnet test test\Microsoft.Health.Fhir.R4.Tests.Integration\Microsoft.Health.Fhir.R4.Tests.Integration.csproj --filter "FullyQualifiedName~SqlServerGuardedNoOpTests\|FullyQualifiedName~SqlServerSetMergeTests\|FullyQualifiedName~SqlServerTransactionScopeTests\|FullyQualifiedName~SqlServerWatchdogTests"` | 20 passed, 0 failed, 0 skipped (live SQL Server) |
| `git diff --check` | clean (exit 0) |

The live guarded-no-op suite is the one that matters most for this round: it still proves on a
real database that the enlisted probe holds its clustered-key lock for the life of a bundle,
that contention surfaces as a 409, and - the assertion this round could have broken - that
`dbo.Transactions` gains no uncompleted row when a bundle transaction returns early with a
failed precondition. Bounding the attempt did not cost that settlement.

### Self-review

- The bound is not a lone timeout that a slow-but-alive store could still outlast in the retry
  loop. Retries are disabled, the command timeout is pinned, and the CTS covers connection
  acquisition as well as execution, so all three of the ways the old form could reach minutes
  are closed rather than one.
- The change deliberately does *not* link the caller's token. Linking is the obvious "bound the
  cleanup" fix and is wrong here, because cancellation is the most common way to reach
  disposal untransferred; `MergeAsync_GivenCallerCancellationBeforeMergeResources_...` fails
  if a later change makes that mistake.
- Exception semantics were re-verified at both levels: `Assert.Same(probeFailure, thrown)`
  proves the caller receives the original `SqlException` instance both when settlement throws
  its own `SqlException` and when settlement is abandoned by its bound.
- Cleanup is logged, never dropped. `DisposeAsync_WhenSettlementIsAbandoned_ThenTheWarningIdentifiesTheTransaction`
  and `DisposeAsync_WhenSettlementFails_ThenItLogsAWarningAndDoesNotThrow` assert the warning
  carries the transaction id and the causing exception, which is what makes an abandoned
  transaction traceable to the watchdog recovery that follows.
- The refactor of the non-query test seam kept `ConfigureSuccessfulNonQueryCalls` behaviorally
  identical (both helpers now delegate to one `ConfigureNonQueryCalls`), so all Round 4
  settlement and contention tests still assert exactly what they asserted before; they pass
  unchanged.
- An independent code review of the diff against the five contract points (attempt preserved,
  bounded and not caller-token-only, original exception preserved, own connection and
  idempotent, nothing else weakened) reported no high-confidence defects.

### Remaining limitations

- 5 seconds is a compiled constant, not configuration, matching the existing 10-second probe
  lock timeout precedent. A store that is merely slow rather than broken can now hand the
  watchdog a transaction it would previously have settled in band; that is the deliberate
  trade, since the watchdog already owns recovery and the alternative is a multi-minute
  failure latency.
- The bounded path is proven by deterministic seams, not by a live datastore outage: inducing
  a genuine multi-minute SQL stall in the integration fixture is not something this
  environment can do reproducibly. The live suite proves the *successful* settlement path
  still works end to end.
- Round 4's limitations are unchanged and still apply: hard delete and purge-history keep the
  approved read/validate-before-delete gap, SearchParameter deletion has no version-bearing
  datastore operation, Cosmos has no live emulator coverage, and `FhirStorageTests(SqlServer)`
  retains 4 pre-existing environment failures.

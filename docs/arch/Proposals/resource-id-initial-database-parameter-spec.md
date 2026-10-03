# Configurable resource ID length

**Status:** Proposed  
**Date:** 2026-10-03  
**Scope:** Application configuration for all stores; operator-managed SQL widening.

## Context

An Epic integration supplies resource IDs longer than FHIR's 64-character
limit (an observed ID has 88 characters), with other resources referencing
those IDs verbatim. Substituting another identifier is not acceptable for
this integration. Allowing longer resource IDs is an explicitly
non-conformant extension, not a change to the FHIR `id` datatype.

SQL capacity is not just `dbo.Resource.ResourceId`: reference indexes,
change-data tables, table-valued parameters (TVPs), procedure arguments,
and an export table variable also carry IDs. Narrow client metadata can
silently truncate an ID before SQL receives it. This proposal retains
that database inventory but makes configuration, not database inspection,
the application contract.

## Decision

Use **one setting**, `CoreFeatureConfiguration.MaxResourceIdLength`
(`int`, default **64**):

| Surface | Name or behavior |
| --- | --- |
| Configuration path | `FhirServer:CoreFeatures:MaxResourceIdLength` |
| Environment variable | `FhirServer__CoreFeatures__MaxResourceIdLength` |
| Supported range | 64..256, inclusive |
| Startup validation | In `AddFhirServer`, after configuration binding and `configureAction`; values outside the range throw and the server fails to start. |
| SQL ownership | The operator widens the database to the same N before raising the setting. |

There is **no database read** to discover or validate this maximum, no
persisted database setting, and no schema-version change. The operator
owns agreement between configuration and physical SQL widths. Changing
the setting requires restarting the server instances; it is not a
runtime database-capacity negotiation.

At the default 64, behavior, generated SQL, `SqlParameter` sizes, and TVP
metadata are identical to before. This includes the pre-existing quirk:
`_id` and SMART compartment-root search values longer than the configured
maximum are sized to that maximum, and SqlClient truncates them, producing
prefix matching. At 64 this affects over-64 values; with a raised setting
it affects values longer than N. **That search behavior is out of scope
and is not repaired here.**

### Application surfaces

Every resource-ID width below uses the same configured N. Unrelated
element IDs and version IDs retain their existing contracts.

| Surface | Required behavior |
| --- | --- |
| `IdValidator<T>` | Use `^[A-Za-z0-9\-\.]{1,N}$`. Preserve the alphabet, null-valid behavior, and the existing .NET `$` trailing-newline quirk. |
| Request validation | The validator is used by `ResourceElementValidator` for Create, Upsert, and MemberMatch, by `$validate`'s `ValidateResourceOperationValidator`, and by Get/Delete validators. |
| Error messages | Format `Resources.IdRequirements` with N, including its use in `ValidateIdSegmentAttribute`. |
| `$import` | `ImportResourceIdValidator` uses N through both Firely and Ignixa import parsers; retain the required-ID check. |
| Reference parsing | `ReferenceSearchValueParser` changes the resource-ID capture to `{1,N}`. The `_history` version capture stays bounded at 64. Otherwise accepted long references would silently index as 64-character prefixes. Preserve relative/absolute and external-reference semantics. |
| Reference row generation | `ReferenceSearchParamListRowGenerator` truncates at N rather than the generated 64-wide column metadata. |
| Scalar query parameters | `HashingSqlQueryParameterManager` centrally sizes parameters for `ResourceId`, `ReferenceResourceId`, and `ReferenceResourceId1` to N. Sizes and generated SQL are identical at 64. |
| SQL TVPs | Five hand-written subclasses of the generated definitions override `Columns`, widening only the ID column to N: ResourceList, ReferenceSearchParamList, ReferenceTokenCompositeSearchParamList, ResourceKeyList, and ResourceDateKeyList. At 64 their metadata is unchanged. |
| Hard delete | For an ID longer than 64, send the full ID with sufficient client parameter size and additionally send `@ExpectedResourceIdLength int` to `dbo.HardDeleteResource`. IDs of length 64 or less send exactly today's parameters. |

The generated TVP definitions hard-code `varchar(64)` `SqlMetaData`.
SqlClient silently truncates over-width values client-side when filling
`SqlDataRecord`; changing server types alone does not fix that. The five
small subclasses leave generated code, row shapes, column order,
non-ID metadata, and SQL type names unchanged. All bindings for these
five TVPs must use the corresponding definition, including reads,
writes, import, and reindex.

For long hard deletes, an unpatched procedure rejects the extra argument
with "too many arguments" instead of deleting a truncated-prefix ID.
The operator-patched procedure must widen `@ResourceId` to `varchar(N)`,
declare `@ExpectedResourceIdLength int = NULL`, and, when it is supplied,
**THROW if `LEN(@ResourceId) <> @ExpectedResourceIdLength` before any
mutation**. In particular, put the check before
`MergeResourcesBeginTransaction`, not just before the resource DELETE or
UPDATE. The application supplies the original ID length. The nullable
default preserves the existing short-ID call shape; the check detects a
still-narrow procedure argument even if the new guard was added.

### Unchanged validation and storage boundaries

Profile / `$validate` StructureDefinition validation keeps the FHIR-spec
64-character rule. Thus the configurable entry validator can accept a
long resource ID while `$validate` against a profile reports it invalid.
Do not relax every FHIR `id`-typed element or change SDK-wide primitive
validation. Retain coverage for the Firely and Ignixa parsing paths so an
SDK change cannot independently reject IDs accepted by this contract.

Cosmos DB has **no code changes**. It has no fixed SQL-style column width,
so raising this shared setting simply permits longer resource IDs,
subject to Cosmos's own ID and partition-key limits. SQL widening
instructions do not apply to Cosmos.

## Operator SQL contract

For SQL, use an operator-owned script to widen **all active ID-bearing
objects to the same N**. The inventory below is confirmed against
`src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql` in this worktree.
Inspect the actual deployed schema and dependencies as well: historical
schema versions and operator customizations can differ.

### Columns, indexes, and views

| Object | Required change and dependencies |
| --- | --- |
| `dbo.Resource.ResourceId` | `varchar(N) COLLATE Latin1_General_100_CS_AS NOT NULL`; preserve the unique version index and filtered current-resource index. |
| `dbo.ReferenceSearchParam.ReferenceResourceId` | `varchar(N) COLLATE Latin1_General_100_CS_AS NOT NULL`; preserve its unique reference-ID index. |
| `dbo.ReferenceTokenCompositeSearchParam.ReferenceResourceId1` | `varchar(N) COLLATE Latin1_General_100_CS_AS NOT NULL`; preserve its composite index and included columns. |
| `dbo.ResourceChangeData.ResourceId` | `varchar(N) NOT NULL`; preserve its existing collation, defaults, partitioning, and index layout. |
| `dbo.ResourceChangeDataStaging.ResourceId` | `varchar(N) NOT NULL`; preserve its existing collation and staging constraints. Widen together with ResourceChangeData for partition-switch compatibility. |
| Dependent views | Run `sp_refreshview` for non-schema-bound views, including `dbo.CurrentResource`, after widening their base columns. |

Affected ID-bearing indexes in the current definitions are:

- `IX_Resource_ResourceTypeId_ResourceId_Version`
- `IX_Resource_ResourceTypeId_ResourceId` (`WHERE IsHistory = 0`)
- `IXU_ReferenceResourceId_ReferenceResourceTypeId_SearchParamId_BaseUri_ResourceSurrogateId_ResourceTypeId`
- `IX_SearchParamId_ReferenceResourceId1_Code2_INCLUDE_ReferenceResourceTypeId1_BaseUri1_SystemId2`

Restate `COLLATE Latin1_General_100_CS_AS` and `NOT NULL` explicitly for
the resource/reference columns. The two change-data definitions do not
declare that case-sensitive collation; preserve their actual existing
collation rather than changing it as a side effect of widening.
Preserve all uniqueness, filters, included columns, compression,
partition schemes, and constraints. None of the required active table
ID columns is in a primary key. Widening an indexed `varchar` can be
supported without dropping an ordinary index, but establish the exact
DDL/rebuild order on the deployed Azure SQL shape. If rebuilding is
required, follow the repository's online-index guidance.

At N = 256, the declared reference-ID unique key totals approximately
398 bytes, while the reference-token composite key totals 514 bytes
(`SearchParamId`, `ReferenceResourceId1`, `Code2`), before implicit
partition/clustering additions. Both remain below the relevant 900-byte
clustered and 1,700-byte nonclustered limits. The composite key, not the
approximately 400-byte reference key, is the wider declared key.

`dbo.CurrentResource` in `Tables/Resource.sql` is a temporary stand-in
for code generation, immediately dropped. The deployed object is the
non-schema-bound view in `Views/CurrentResource.sql`; do not treat it as
a second permanent table requiring `ALTER COLUMN`.

`dbo.CompartmentAssignment.ReferenceResourceId` is optional, not part of
the active write contract. No current procedure under `Sql/Sprocs`
inserts into this table; `DeleteHistory` still deletes from it.
`SqlCompartmentSearchRewriter` uses reference search indexes for
materialized compartment membership. Leave this legacy column at 64
unless the deployed system has a writer that requires widening. If it
does, also account for its clustered primary key
`PKC_CompartmentAssignment` and filtered nonclustered index; it is not
covered by the ordinary indexed-column widening assumption above.
Older generated compartment TVP bindings are not among the five active
TVPs changed by this proposal.

### TVP types and dependent procedures

SQL table types cannot be altered. Drop/recreate the following types
with only their ID column widened, preserving column order, collation,
nullability, and primary/unique constraints:

| SQL type | ID column |
| --- | --- |
| `dbo.ResourceList` | `ResourceId` |
| `dbo.ResourceKeyList` | `ResourceId` |
| `dbo.ResourceDateKeyList` | `ResourceId` |
| `dbo.ReferenceSearchParamList` | `ReferenceResourceId` |
| `dbo.ReferenceTokenCompositeSearchParamList` | `ReferenceResourceId1` |

Inventory and drop/recreate dependent procedures in dependency order,
preserving their definitions, permissions, and other deployment
properties. Current source dependencies include `MergeResources`,
`MergeResourcesAndSearchParams`, `UpdateResourceSearchParams`,
`CaptureResourceIdsForChanges`, `GetResources`, and
`GetResourceVersions`. `UpdateResourceSearchParams` also declares local
variables of the reference types; auditing procedure signatures alone
is insufficient. Restore the same type/procedure names; the application
does not switch on a schema version for these operator-managed objects.

### Procedure parameters and internal storage

| Object | Required contract |
| --- | --- |
| `dbo.HardDeleteResource` | Widen `@ResourceId` to `varchar(N)` and add the nullable expected-length argument and pre-mutation guard described above. |
| `dbo.CaptureResourceChanges` | Widen `@resourceId` to `varchar(N)`. |
| `dbo.GetResourcesByTypeAndSurrogateIdRange` | Widen the local `@ResourceIds TABLE (ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS PRIMARY KEY)` to `varchar(N)`; retain its key and collation. |

`CaptureResourceChanges` is effectively unused by current paths:
`MergeResources` actually calls `CaptureResourceIdsForChanges`, which
receives `dbo.ResourceList`. A stale comment still names the scalar
procedure. Widen it to maintain the declared database contract, but do
not infer that widening it fixes active change capture. The active path
requires the ResourceList client/server widths and both change-data
tables. A legacy caller of a narrow scalar proc could silently truncate
its ID; it has no expected-length guard.

The export table variable is exercised by the snapshot branch
(`@GlobalEndId IS NOT NULL AND @IncludeHistory = 0`) when selecting
historical IDs. Its width is not exposed through `sys.parameters`;
verify the procedure definition and exercise that branch, not just a
current-resource export.

Do **not** blanket-replace `varchar(64)`. Leave reindex job IDs and
their procedure arguments, `EventAgentCheckpoint.CheckpointId` and
`LastProcessedIdentifier` (and corresponding arguments),
`SearchParamHash` in tables/TVPs, and `EventLog.HostName` unchanged.
Reference version fields remain integers, and the parser's history
version bound remains 64. No other active 64-wide resource-ID contract
was found in the current SQL object definitions.

## Deployment and migration risks

1. Choose N in 64..256 and prepare a version-specific operator script.
   Back up the database; establish a maintenance window, sufficient log
   space, DDL locking/rebuild behavior, and recovery for partial changes.
   Stop conflicting application/schema maintenance during type recreation.
2. **Widen the database first.** Verify columns, types, parameters, indexes,
   collations, nullability, procedure bodies, the hard-delete guard, and
   refreshed views. A retry must recognize completed changes and fail
   visibly on an unexpected or incompatible partial shape.
3. Verify long-ID round trips and the guarded delete/export paths in a
   disposable environment, then raise the setting to the same N and
   restart all server instances.

This is not restricted to new databases, but widening populated indexed
columns is an operator-scheduled migration, not an application startup
action. Existing accounts need their own locking, log-space, recovery,
and downtime assessment. A new widened account with `$export`/`$import`
is an alternative when in-place changes are unsuitable.

**Never lower the setting after IDs longer than the new value exist.**
Besides rejecting GET/DELETE and new writes for those IDs, a lower
reference-parser/row-generation bound can truncate their reference
indexes during reindex. Rolling back to a default-64 binary is likewise
unsafe once long IDs exist. Preserve the script and setting through
restore, disaster recovery, and deployment changes.

Schema upgrades shipped by fhir-server may recreate these columns,
types, procedures, or table variables at 64, silently or loudly undoing
the script. **The operator must verify and re-apply it after upgrades,
before resuming long-ID traffic.** There is no startup capacity check
to catch drift automatically. A live type replacement requires its own
coordination; this proposal makes no rolling-DDL guarantee.

`SwitchPartitionsOut` uses `SELECT * INTO ... WHERE 1 = 2`, which copies
the current physical column widths. Verify switch-out, switch-in, and
cleanup against the widened database, including any switch tables
created before widening. `RemovePartitionFromResourceChanges_2`
switches into ResourceChangeDataStaging, so that pair must remain
compatible.

The shipped full snapshot is generated from the SQL sources; upgrade
diffs are maintained separately. Neither is changed by this proposal.
The baseline snapshot-versus-diff equivalence test remains unchanged;
an operator-widened database is intentionally outside that baseline.
Do not edit historical snapshots or claim that a database parameter
changes a generated snapshot's physical shape.

### Configuration/database drift

| Drift or boundary | Outcome |
| --- | --- |
| Setting > database columns | Writes fail loudly with TVP/insert truncation errors such as SQL 2628/8152 when values exceed those columns. |
| Server TVP types not widened | N-wide client metadata sends the value intact; a value wider than the server type fails loudly during TVP materialization. |
| Procedure parameters not widened | Long HardDelete calls fail loudly: an unpatched proc rejects the extra argument; a proc with the guard but a narrow ID argument throws on the length mismatch before mutation. CaptureResourceChanges is effectively unused by current paths; legacy callers lack this protection. |
| Export table variable still 64 | The affected snapshot-export insertion fails loudly with 2628 (or 8152 on older error-reporting configurations). |
| Setting < database width | Longer stored IDs can be read through search, but validation rejects new long IDs and GET/DELETE of long IDs: loud, user-visible failures. Lower-bound reference indexing can also truncate on reindex. |
| Search value > configured N | Parameters are sized at N regardless of database width; existing SqlClient truncation/prefix matching remains. |

These outcomes assume the proposed client TVP definitions and parameter
sizing are in use. A remaining generated 64-wide client TVP can silently
truncate before SQL sees a mismatch, even when the database is wide.

## Alternatives considered

Widening the TVP types in the shipped schema pipeline was rejected for
this scope. It requires new type/procedure names, schema version 118,
version-conditional C#, and changes to default metadata. The database
is operator-managed here, so five small client definition subclasses
provide the required width without changing the default contract.

Uniformly widening all populated databases is a separate versioned
migration decision. Likewise, integer resource-ID mapping and resource
table restructuring are separate proposals, not prerequisites for
accepting longer IDs.

## Verification expectations

Application implementation should cover absent/default configuration,
64 and 256, invalid values below/above the range, and overrides through
`configureAction`. At 64, assert unchanged SQL, scalar parameter sizes,
TVP metadata, validation messages, and existing over-limit search behavior.
For raised N, cover 1, 64, 65, the observed 88, N, and N+1 characters,
the unchanged alphabet/null/newline behavior, both import parsers,
relative/absolute/versioned references, and profile-validation boundaries.

Database acceptance requires exact ID/reference round trips across CRUD,
history, bundles, `$import`, reindex, reference/chained/compartment search,
`_include`, change feed, and snapshot `$export`. Verify all five TVPs,
the composite reference index, case-sensitive ID matching, and the
HardDelete failure modes without mutation. Exercise partial script
recovery, partition maintenance, and re-application after a representative
schema upgrade on a database containing IDs longer than 64. Never hash,
rewrite, case-fold, or substitute the original resource ID.

## Consequences

The application has one startup-validated setting and no SQL
initialization lifecycle for this feature. Operators acquire explicit
responsibility for SQL capacity and upgrade drift. Long IDs and their
references remain non-conformant and may be rejected by downstream
systems; profile validation intentionally continues to report that.

## References

- [FHIR R4 `id` datatype](https://hl7.org/fhir/R4/datatypes.html#id)
- [SQL development and migration guidance](../../../src/Microsoft.Health.Fhir.SqlServer/readme.md)
- [SQL Server user-defined table types](https://learn.microsoft.com/en-us/sql/t-sql/statements/create-type-transact-sql)
- [SQL Server capacity limits](https://learn.microsoft.com/en-us/sql/sql-server/maximum-capacity-specifications-for-sql-server)
- [.NET environment configuration](https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration-providers)
- [ResourceIdIntMap proposal](adr-2502-ResourceIdIntMap.md)
- [Resource table refactor proposal](adr-2502-Resource-table-refactor.md)

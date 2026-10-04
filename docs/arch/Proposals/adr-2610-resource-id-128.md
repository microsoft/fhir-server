# ADR-2610: Resource IDs up to 128 characters (SQL schema 118 and MaxResourceIdLength)

**Status**: Proposed
**Date**: 2026-10-03
**Feature**: resource-id-128

## Context

Some integrations supply resource IDs longer than the FHIR limit of 64 characters. One observed ID has 88 characters, and other resources reference it. The server rejects these IDs at the API, in import, and in reference parsing. The SQL Server store also caps them at 64: every ResourceId column, five table types, and three procedures declare `varchar(64)`.

The SQL limits fail in two ways. Table types and columns reject an over-long value loudly. Scalar procedure parameters truncate it silently. A truncated `@ResourceId` on hard delete could address a different resource whose ID is the 64-character prefix. Any fix has to remove both limits for the customers who need longer IDs. It must leave the default behavior unchanged for everyone else and stay safe while old and new app instances run against the same database.

## Options Considered

1. **Per-customer schema width chosen at database creation, with app-side TVP and parameter overrides** - an operator script widens one database to N, and the app sizes TVP metadata and proc parameters to N *(rejected: forks the schema per customer, later schema upgrades silently undo the script, and the app must carry width-specific TVP definitions)*
2. **Widen everything to 128 for all customers in one schema version, with validation controlled by a setting** - one schema for everyone; a validation-only setting decides what IDs are accepted *(chosen)*
3. **Widen to 255 to match Cosmos DB** - same as option 2 with a larger bound *(rejected: no requirement beyond 88 characters, and index key size grows with the bound)*
4. **Replace table types with new names (`...V2`)** - avoid touching the existing types *(rejected: every dependent proc and the app must switch names together, which needs a two-version dance; same-name replacement measured compatible with old app code)*

## Decision

Schema version 118 widens every ResourceId column, the five table types that carry a ResourceId (`ResourceList`, `ResourceKeyList`, `ResourceDateKeyList`, `ReferenceSearchParamList`, `ReferenceTokenCompositeSearchParamList`), and the scalar ResourceId parameters of `HardDeleteResource`, `CaptureResourceChanges` and `GetResourcesByTypeAndSurrogateIdRange` from `varchar(64)` to `varchar(128)` for all customers. Columns are altered in place, restating each column's current collation and nullability. On SQL Server this is a metadata-only change. Measured on 500k to 1M row tables, each ALTER took tens of milliseconds and wrote a log volume that scales with partition count, not rows. Page counts and partition IDs were unchanged. `ResourceChangeData` and `ResourceChangeDataStaging` are widened together so the change-feed partition switch keeps working. Leftover import intermediates from `SwitchPartitionsOut` are widened too, so an in-flight initial-mode import can still switch back in. The `CurrentResource` view is refreshed.

Table types cannot be altered. The diff replaces them under their existing names in one transaction. It alters the dependent procedures to stubs, drops and recreates the types, then restores the procedures. Callers never see a missing procedure. A caller whose TVP call races the swap can get error 2766 (type definition changed), which is now retriable. Measured under concurrent merge load, the swap took 200 to 300 ms and produced only 2766 errors. The existing precedent of dropping the procedures first produced "procedure not found" errors, and taking type locks first produced multi-second swaps and deadlocks. Every step checks the current width, so the diff can be rerun.

The app gains `CoreFeatures:MaxResourceIdLength` (environment variable `FhirServer__CoreFeatures__MaxResourceIdLength`). It defaults to 64 and accepts 64 through 128; other values fail at startup. It controls validation only: API ID checks, route ID segments, import, and reference parsing. Search parameter declarations for ResourceId columns follow the setting, so default customers keep today's generated SQL. TVP metadata needs no change because the regenerated types are 128 wide and SQL Server accepts narrower client metadata. The SQL data store refuses writes when the setting is above 64 and the current schema version is below 118. That check runs before any database call on merge, hard delete and reindex, because that is the earliest point where the current schema version is known. Profile and `$validate` StructureDefinition validation keep the FHIR rule of 64.

## Consequences

- One schema for every customer. No operator script, and upgrades cannot undo the widening.
- Customers who keep the default of 64 see no behavior change beyond the one-time schema upgrade.
- Rollout is two-phase. Upgrade to schema 118 with the setting at 64, wait until no instance runs a binary older than schema 118, then raise the setting. Old binaries keep working on schema 118 because their TVP metadata is accepted and their scalar parameters only ever carry IDs of 64 characters or fewer.
- Old binaries may surface a single 2766 error on a TVP call that races the type swap. New binaries retry it.
- Rolling the setting back to 64 after long IDs exist makes those resources unreachable by ID through the API. Lowering the setting is not supported once long IDs are stored.
- `CompartmentAssignment.ReferenceResourceId` stays at 64. The table has no active writer; dropping it is separate cleanup.
- Customer-created objects that depend on a ResourceId column (for example schema-bound views) block the ALTER and must be removed before upgrading.
- A resource whose ID is longer than 64 characters fails `$validate` against a profile, because the FHIR `id` type still allows 64. This is intentional.
- Search values longer than the setting are still truncated by SqlClient parameter sizing, as they are today at 64. A truncated value can match a stored ID equal to its prefix. This is existing behavior, unchanged here.
- Cosmos DB needs no change; it already allows 255-character IDs.

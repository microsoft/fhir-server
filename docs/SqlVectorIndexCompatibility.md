# Azure SQL vector index compatibility

Documentation and execution assessed on **2026-09-21**. This assessment accompanies schema V118 and distinguishes
native vector storage from future approximate nearest-neighbor indexing. The current MVP uses exact
cosine ranking and does not install or enable a DiskANN index.

## Minimum engine requirement

Schema V118 requires an engine that provides the native `vector` type: **Azure SQL Database, or
SQL Server 2025 or later**. The V118 migration probes `sys.types` and raises error 50419 before any
vector DDL when the type is absent. A fresh install is guarded by the vector DDL itself, which fails
inside the initialization transaction on an engine without the type.

This applies to development environments as well as deployments:

| Environment | Requirement |
|---|---|
| Docker samples (`samples/docker`, `release/`) | Pinned to `mcr.microsoft.com/mssql/server:2025-latest`. An untagged `mssql/server` resolves to SQL Server 2022 and has no `vector` type. |
| Integration tests | `SqlServerFhirStorageTestsFixture` defaults to `server=(local)`; that instance must be SQL Server 2025 or later. |
| CI | Already satisfied — the SQL test jobs target Azure SQL Database. |

SQL Server 2019 and 2022 deployments cannot upgrade to V118 and remain at V117. See
[ADR-2608](arch/adr-2608-sql-semantic-search.md) for the alternatives considered and rejected.

## General Purpose and Hyperscale

This document concerns **Azure SQL Database**, not the General Purpose tier of Azure SQL Managed
Instance.

| Deployment | Documented support | Limit of the evidence |
|---|---|---|
| General Purpose | Microsoft's `CREATE VECTOR INDEX` reference applies to Azure SQL Database and describes DiskANN in preview. | The reference does not provide an explicit GP provisioned/serverless DiskANN eligibility matrix. Product-level applicability is not an execution result for a particular database. |
| Hyperscale | The Hyperscale FAQ explicitly states: "The vector capabilities of the SQL Database Engine are available in Hyperscale databases," and references `VECTOR_SEARCH` and `VECTOR_DISTANCE`. | This confirms native vector capabilities, not certification of this exact table/index configuration across every compute offering and region. |

The [serverless overview](https://learn.microsoft.com/en-us/azure/azure-sql/database/serverless-tier-overview?view=azuresql)
lists both General Purpose and Hyperscale. That establishes the available compute models, not a
separate DiskANN guarantee. Check the
[regional availability page](https://learn.microsoft.com/en-us/azure/azure-sql/database/region-availability?view=azuresql#vector-search)
and the actual database's vector index version before enabling the preview.

Sources:

- [CREATE VECTOR INDEX: applicability and current limitations](https://learn.microsoft.com/en-us/sql/t-sql/statements/create-vector-index-transact-sql?view=azuresqldb-current#limitations-and-considerations).
- [Hyperscale FAQ: vector data](https://learn.microsoft.com/en-us/azure/azure-sql/database/service-tier-hyperscale-frequently-asked-questions-faq?view=azuresql#does-azure-sql-database-hyperscale-support-vector-data).

## V118 table assessment

The schema stores `Embedding vector(1536) NOT NULL` on a nonpartitioned base table with the clustered
primary key `(ResourceTypeId, ResourceSurrogateId, SearchParamId, EmbeddingModelId, ChunkOrdinal)`, PAGE compression,
and a separate `SourceTextCompressed varbinary(max) NOT NULL` payload.

| Property | Assessment against current documentation |
|---|---|
| Native `vector(1536)` and cosine distance | Supported type/metric. The vector type permits up to 1,998 dimensions. |
| Nonpartitioned base table | Matches the stated prerequisite; vector indexes cannot be partitioned. |
| Clustered primary key | Matches the stated prerequisite. The current documentation says "The table must have a primary key clustered index"; it does not state the older single-integer-key restriction. |
| This five-column composite key | Index creation and indexed DML succeeded in the General Purpose execution below. This is not certification of every tier, region or index format. |
| PAGE compression plus compressed binary payload | The exact combination succeeded in the execution below. Off-row data is not compressed by PAGE compression; the application separately compresses passage bytes. |

The migration's `sys.types` probe confirms the native vector type only. It does not prove that a
DiskANN index can be created or that a query will use it. The execution below validates one Azure
SQL General Purpose configuration; Hyperscale and other offerings have not been execution-tested.

Model identity is part of the table key, TVP uniqueness constraint and application deduplication
key. The runtime MVP still uses one configured model; these keys do not enable model-preserving
reindexing, side-by-side backfill or query cutover.

## Recorded execution: General Purpose, 2026-09-21

The finalized schema was deployed to a newly created, disposable Azure SQL database. No existing
application database was modified. The database, server and dedicated resource group were removed
after validation.

The vector schema was numbered V117 at execution. It was renumbered to V118 afterwards, when V117
was assigned to the unrelated `GetMostRecentJob` migration. The vector migration's only change was
the version in its engine-guard message; the renumbered V116 → V117 → V118 upgrade chain was
validated locally on SQL Server 2025.

| Property | Observed value |
|---|---|
| Offering | General Purpose, provisioned Gen5, 2 vCores (`GP_Gen5_2`), West US 2 |
| Engine | Microsoft SQL Azure `12.0.2000.8`, build dated August 19, 2026 |
| Schema | Generated full snapshot (then V117), five-column clustered PK, PAGE compression, compressed passage payload, `vector(1536)` |
| Index | Cosine DiskANN; `sys.vector_indexes.build_parameters` reported version `3` |
| Population | 1,000 deterministic nonzero seed vectors inserted through `MergeResources`, plus isolated lifecycle-test rows |
| Query evidence | Actual execution plan contained `Vector Index Seek`; diagnostic `VECTOR_SEARCH` used `FORCE_ANN_ONLY` |

With the index present, the following checks passed:

- Legacy callers of both merge procedures omitting the vector TVP.
- Insertion of model-distinct rows with identical other key columns.
- Idempotent merge retry and recovery of missing single-model vectors.
- Resource version updates, historical-vector cleanup and purge-history preservation of current vectors.
- Reindex replacement, evaluated-empty removal, omitted-input preservation and input-subset/version guards.
- Wrong-dimension failure inside reindex, preserving the original ordinary hash and vector payload.
- Hard deletion and absence of the deleted vector from forced-ANN results.
- Model, SearchParameter and resource filters, with post-commit results checked on separate connections.
- Reapplication of the actual vector migration (then `117.diff.sql`), preserving row count and the version-3 index.

Local SQL Server 2025 tests separately passed full-snapshot/migration-chain equivalence and
reapplication, as well as table/TVP acceptance of distinct models and rejection of duplicate full
keys. Azure execution used the full snapshot followed by vector-migration reapplication; it did not
repeat the entire historical upgrade chain in Azure.

ANN quality was measured, not certified. Ten filtered recall-at-10 probes averaged **0.57** on a
two-dimensional circle padded to 1,536 dimensions. A second deterministic fixture with 50 dense
1,536-dimensional clusters averaged **1.0** across ten probes, and the indexed lifecycle checks
passed again. These small synthetic results are workload-dependent, not a production recall or
performance guarantee. No stale-index setting was enabled. The production MVP continues to use
exact distance queries; enabling an approximate FHIR query path requires separate acceptance of
recall, authorization, resource-level deduplication and paging.

Additional references:

- [Native vector type](https://learn.microsoft.com/en-us/sql/t-sql/data-types/vector-data-type?view=azuresqldb-current).
- [Row and page compression](https://learn.microsoft.com/en-us/sql/relational-databases/data-compression/data-compression?view=azuresqldb-current).

## Requirements before future DiskANN enablement

The current Azure SQL documentation describes latest-format vector indexes (version 3), with full
INSERT, UPDATE, DELETE and MERGE support and iterative filtering. Older index formats have different
DML and filtering limitations. A read-only or stale-index workaround is not an acceptable substitute
for the MVP's transactional write contract.

Before enabling approximate search, execute the following against isolated, explicitly selected
General Purpose and Hyperscale databases:

1. Apply the actual V118 schema, retaining its composite key, PAGE compression and binary payload.
   Populate at least 100 distinct, non-null vectors; index creation has a minimum row requirement.
2. Create the cosine DiskANN index and record the service tier, compute model, region, database
   version and vector index version. Creation on one tier does not certify the other.
3. Exercise resource merge, reindex replacement, evaluated-empty deletion, rollback and hard delete
   through the existing procedures. Check result visibility after commit.
4. Exercise an explicit approximate query and inspect its plan. `VECTOR_SEARCH` may choose exact
   kNN instead; a successful query alone does not prove DiskANN was used.
5. Verify model/SearchParameter filters, resource authorization, structured predicates,
   resource-level deduplication, score thresholds and the intended paging contract. Approximate
   chunk retrieval is not automatically equivalent to exact ranking of distinct FHIR resources.

Latest-format queries use `SELECT TOP (N) WITH APPROXIMATE` with `VECTOR_SEARCH`; the legacy `TOP_N`
argument is not supported by version 3 indexes. Adding an index alone does not change the current
`VECTOR_DISTANCE` query into approximate search or establish an acceptable recall contract.

Index creation belongs to a separate populated-database enablement operation, not the empty-table
V118 migration. Current limitations also prohibit `TRUNCATE TABLE` while a vector index exists and
deployment of vector indexes through DacPac/BACPAC; operational import/export and rebuild procedures
must account for those restrictions. This document does not authorize changes to shared databases.

- [VECTOR_SEARCH syntax and filtering](https://learn.microsoft.com/en-us/sql/t-sql/functions/vector-search-transact-sql?view=azuresqldb-current).
- [Index versions, DML and deployment restrictions](https://learn.microsoft.com/en-us/sql/t-sql/statements/create-vector-index-transact-sql?view=azuresqldb-current).
- [Azure SQL DiskANN improvements and rollout](https://devblogs.microsoft.com/azure-sql/diskann-vector-index-improvements/).

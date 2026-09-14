# Azure SQL vector index compatibility

Documentation assessed on **2026-09-14**. This assessment accompanies schema V117 and distinguishes
native vector storage from future approximate nearest-neighbor indexing. The current MVP uses exact
cosine ranking and does not install or enable a DiskANN index.

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

## V117 table assessment

The schema stores `Embedding vector(1536) NOT NULL` on a nonpartitioned base table with the clustered
primary key `(ResourceTypeId, ResourceSurrogateId, SearchParamId, ChunkOrdinal)`, PAGE compression,
and a separate `SourceTextCompressed varbinary(max) NOT NULL` payload.

| Property | Assessment against current documentation |
|---|---|
| Native `vector(1536)` and cosine distance | Supported type/metric. The vector type permits up to 1,998 dimensions. |
| Nonpartitioned base table | Matches the stated prerequisite; vector indexes cannot be partitioned. |
| Clustered primary key | Matches the stated prerequisite. The current documentation says "The table must have a primary key clustered index"; it does not state the older single-integer-key restriction. |
| This four-column composite key | No explicit current example or guarantee was found for the exact key. Do not infer either incompatibility or proven compatibility solely from the absence of a restriction. |
| PAGE compression plus compressed binary payload | PAGE compression is supported for Azure SQL rowstore tables, but the vector-index documentation does not explicitly certify this combination. Off-row data is not compressed by PAGE compression; the application separately compresses passage bytes. |

The migration's `sys.types` probe confirms the native vector type only. It does not prove that a
DiskANN index can be created or that a query will use it. No Azure SQL GP or Hyperscale execution
test of this exact layout was performed for this documentation assessment.

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

1. Apply the actual V117 schema, retaining its composite key, PAGE compression and binary payload.
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
V117 migration. Current limitations also prohibit `TRUNCATE TABLE` while a vector index exists and
deployment of vector indexes through DacPac/BACPAC; operational import/export and rebuild procedures
must account for those restrictions. This document does not authorize changes to shared databases.

- [VECTOR_SEARCH syntax and filtering](https://learn.microsoft.com/en-us/sql/t-sql/functions/vector-search-transact-sql?view=azuresqldb-current).
- [Index versions, DML and deployment restrictions](https://learn.microsoft.com/en-us/sql/t-sql/statements/create-vector-index-transact-sql?view=azuresqldb-current).
- [Azure SQL DiskANN improvements and rollout](https://devblogs.microsoft.com/azure-sql/diskann-vector-index-improvements/).

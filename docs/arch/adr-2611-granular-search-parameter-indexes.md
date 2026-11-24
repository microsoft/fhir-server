# ADR-2611: Granular Search-Parameter Indexes

**Status**: Proposed
**Date**: 2026-11-20
**Feature**: Granular SQL search-parameter indexes

## Context

Each of search-param tables (sometimes also referred to as "index" to increase confusion) has clustered "primary" 
index and one or many shared (used across all relevant search params) secondary indexes. Size of each secondary is 
close to the size of the clustered index. Only small number of search params is actually used by SQL Server query 
optimizer. 

If we create per-parameter filtered indexes, and disable not used ones, we can significantly (close to 2x) reduce total 
"index" size. The goal of this ADR is to evaluate this strategy.

For now we assume that granular secondary indexes remain partitioned by `ResourceTypeId` (no filters on resource type) 
and each index is filtered by `SearchParamId`.

If the customer explicitly consented to use this space saving feature and has run a comprehensive workload, we can look 
at SQL Server index usage data in `sys.dm_db_index_usage_stats` to identify indexes with no observed read activity and 
disable them. Exact workflow supporting this process is a subject for future discussion. 

The granular indexes must support all current searches with no exceptions.

## Example

Two of the four shared nonclustered indexes on `dbo.DateTimeSearchParam` are shown below.
The granular names use `42` as an example `SearchParamId`.

| Before: shared index | After: granular index for parameter 42 |
| --- | --- |
| `IX_SearchParamId_StartDateTime_EndDateTime_INCLUDE_IsLongerThanADay_IsMin_IsMax` | `GIX_StartDateTime_EndDateTime_INCLUDE_IsLongerThanADay_IsMin_IsMax_WHERE_SearchParam_42` |
| `IX_SearchParamId_EndDateTime_StartDateTime_INCLUDE_IsMin_IsMax_WHERE_IsLongerThanADay_1` | `GIX_EndDateTime_StartDateTime_INCLUDE_IsMin_IsMax_WHERE_IsLongerThanADay_1_WHERE_SearchParam_42` |

Both granular indexes filter on `SearchParamId = 42` instead of keying on it. The second also
keeps its existing `IsLongerThanADay = 1` filter. Both retain their date key order, included
columns, and `ResourceTypeId` partitioning.

## Limitations

SQL Server allows [999 nonclustered indexes per table](https://learn.microsoft.com/en-us/sql/sql-server/maximum-capacity-specifications-for-sql-server),
including disabled indexes. Built-in and custom search parameters that target the same table
share this limit. Disabling an unused granular index can reclaim data space but does not free
an index slot.

`DateTimeSearchParam` uses four granular indexes per eligible parameter. With four disabled
shared indexes (used as templates) retained, the table can accommodate at most 248 built-in and custom 
parameters combined: round down `(999 - 4) / 4`. The current FHIR codebase includes 106 eligible built-in 
parameters targeting `DateTimeSearchParam`. If we pre-create all possible granular indexes, this would require 424
granular slots and leave room for 142 custom date parameters.

For `TokenSearchParam`, provisioning 565 built-in parameters uses 565 granular indexes plus
one retained template, leaving room for 433 custom token parameters. 

For `StringSearchParam`, provisioning 135 built-in parameters uses 270 granular indexes plus two retained templates,
leaving room for 363 custom string parameters at two indexes each. Actual capacity depends on
the number of built-in parameters provisioned on each table.

Having more indexes available can increase query compilation time as the optimizer considers
candidate plans and index combinations (see data below).

## Options

1. **Pre-create filtered indexes** - Create a separate index for each eligible search parameter,
   filtered by `SearchParamId` on empty database. This method is simple to implement, but requires a large number 
   of indexes to be created and maintained, even for parameters that are never used. It also increases the time required 
   for query compilation, as the optimizer has to consider more indexes when generating execution plans. It also reduces 
   the number of available index slots for future custom parameters. (Not chosen)

2. **Create indexes on write (synchronously)** - Create a filtered index when a search parameter is first written.
   This avoids building indexes for unused parameters. In order to built index in "zero" time, clustered indexes must 
   be rebuilt with `SearchParamId` as first key, otherwise table will be scanned. This approach exposes SQL Server bug 
   when SQL does not preserve physical sort order of rows for multiple filtered resource types, and adds separate sort 
   operation (expensive). This approach has less limitations than pre-creating all indexes. (Not chosen)
   
3. **Create indexes on write (asynchronously)** - Same as above, but instead of changing clustered indexes, create the index asynchronously 
   by logging into the `JobQueue` table, and letting special process (similar to background task) handle index creation. 
   This leaves a parameter without its index until the write-triggered creation has completed; searches before
   that write need to look at existing indexes before submitting a query. If no indexes (including being created), 
   query should assume no matching data exist and preserve query semantics. For example, search for resources with address=XYZ should 
   return no records, while search for address:missing=true, should return all records. If matching index is being created, query can 
   return "not ready" or resort to a table scan. (Current choice)

## Food for thought

| Mesurement | Curent | Pre-create all | Create-on-write sync | Create-on-write async |
| --- | --- | --- | --- | --- |
| Number of secondary indexes (TokenSearchParam) | 1 | 535 | 57 | 57 |
| SQL CPU on Ingest (PRMS 80), % | 55 | 68 | 62 | 62 |
| Compilation (/?_type=Patient&_id=xyz&_tag=example), msec | 24 | 235 | 47 | 47 |
| Compilation (/?_type=Patient,Practitioner&_id=xyz&_tag=example), msec | 35 | 407 | 97 | 97 |
| Creation of granular index on TokenSearchParam (not existing data), minutes per 1TB |  | 0 | 0 | 10 |

## Current approach

Use the "Create indexes on write (asynchronously)" approach for this experiment. 

## Consequences

- Each parameter-specific index contains only rows for that parameter, targeting less storage and
  smaller per-index maintenance operations; the actual reduction and workload impact require
  measurement.
- Query plans and representative write, rebuild, and storage measurements are needed to validate
  the intended benefits. FHIR query semantics must remain unchanged.

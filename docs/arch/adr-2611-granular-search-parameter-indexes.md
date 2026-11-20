# ADR-2611: Granular Search-Parameter Indexes

**Status**: Proposed
**Date**: 2026-11-20
**Feature**: Granular SQL search-parameter indexes

## Context

Search-parameter tables currently use shared secondary indexes across multiple search parameters.
This ADR evaluates replacing those indexes with per-parameter filtered indexes.
The granular indexes remain partitioned by `ResourceTypeId` on the existing partition scheme.
Each index filters by `SearchParamId` without restricting the index to a single resource type.

Data space can be reclaimed by disabling granular indexes with no observed read activity. If the customer 
explicitely consented to use this space saving feature and has run a comprehensive workload, we can look at SQL Server index usage data in `sys.dm_db_index_usage_stats` to identify 
indexes with no observed read activity and disable them. Exact workflow supporting this process is a subject for future discussion. 

This reclaiming strategy can reduce total search-index storage by up to 2x.

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
shared templates retained and no other nonclustered indexes, the table can accommodate at most
248 built-in and custom parameters combined: round down `(999 - 4) / 4`. The current FHIR codebase
includes 106 eligible built-in parameters targeting `DateTimeSearchParam`, which would require 424
granular indexes and leave room for 142 custom date parameters.

For `TokenSearchParam`, provisioning 565 built-in parameters uses 565 granular indexes plus
one retained template, leaving room for 433 custom token parameters. For `StringSearchParam`,
provisioning 135 built-in parameters uses 270 granular indexes plus two retained templates,
leaving room for 363 custom string parameters at two indexes each. Actual capacity depends on
the number of built-in parameters provisioned on each table.

## Options

1. **Create indexes on write** - Create a filtered index when a search parameter is first written.
   This avoids building indexes for unused parameters, but building one against an already populated
   table can require scanning existing rows. Avoiding that cost depends on changing the clustered
   indexes to lead with `SearchParamId`. This approach exposes SQL Server bug when SQL does not preserve 
   physical order of rows for multiple filtered resource types, and runs separate sort operation (expensive). 
   It also leaves a parameter without its index until the write-triggered creation has completed; searches before
   that write need to look at existing indexes before submitting a query (if no indexes, no data, no resource returned).
   
2. **Pre-create filtered indexes** - Create a separate index for each eligible search parameter,
   filtered by `SearchParamId`, before or independently of writes. Keep the existing resource-type
   partitioning and derive index definitions from the disabled shared-index templates. 

## Current approach

Use the pre-create approach for this experiment. Provision filtered secondary indexes for all
eligible known search parameters, including parameters whose tables currently have no rows.
Retain the existing shared-index templates as the source for key columns, included columns, uniqueness,
and compression. `TokenText` remains on its existing indexing path.

This approach provides an index for each searchable parameter regardless of when its first row is
written and avoids making index creation part of the write path. The create-on-write alternative
remains possible, but requires the `SearchParamId`-leading clustered-index redesign and separate
validation before adoption.

## Consequences

- Each parameter-specific index contains only rows for that parameter, targeting less storage and
  smaller per-index maintenance operations; the actual reduction and workload impact require
  measurement.
- Indexes are provisioned even for parameters with no current data, so we are getting closer 
  to SQL Server's per-table nonclustered-index limit.
- Query plans and representative write, rebuild, and storage measurements are needed to validate
  the intended benefits. FHIR query semantics remain unchanged.

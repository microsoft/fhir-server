## ADR: FHIR Ingested Data Size Calculation

Pull Requests: [Initial Change](https://github.com/microsoft/fhir-server/pull/4856)


### Problem Statement
- Persist the decompressed size of each resource.
- Calculate total data size using ingested volume of resources and total index size

### Context
To support AHDS pricing strategy shift from used storage to ingested volume

### Implementation Details

#### Schema Changes, Resource Persistence Logic, Data Backfill
Add DecompressedLength column to Resource table:
-	Column: DecompressedLength INT NULL
-	Stores the uncompressed size of each resource in bytes
-	Nullable to support gradual rollout and historical data backfill
  
Parameter table entries:
-	FHIR_TotalDataSizeInGB: Stores ( total ingested data size + total index size) in GB
-	FHIR_TotalIndexSizeInGB: Stores total index size in GB
-	Both entries include timestamp of last calculation
  
Modify all resource write operations to:
-	Calculate decompressed size before compression
-	Pass DecompressedLength value to data layer.
-	Populate the new column for all new/updated resources
  
Historical Data Backfill
-	Create a one-time migration script to calculate and populate DecompressedLength for all historical records.
-	Execute updates in batches to minimize performance impact.

#### Background Calculation Job
Implement a periodic background job that runs every 4 hours to:

Calculate metrics:
- Sum of decompressed resource sizes (ingested volume)
- Sum of compressed resource sizes (actual storage)
- Total database used space (from SQL Server DMVs)
- Total index size = Total used space - Compressed resource size
- Total data size = Decompressed resource size + Total index size
 
Persist results:
-	Update Parameters table with new metrics
-   Include timestamp for each update

Emit notification:
-   Publish TotalDataSizeNotification event containing:
-   DateTimeOffset: Timestamp of calculation
-   TotalDataSizeInGB: Total ingested volume + Total index size (decimal)
-   TotalIndexSizeInGB: Index overhead only (decimal)

### Implementation Phases

- Phase 1: Schema Changes, Resource Persistence Logic
- Phase 2: Data Backfill
- Phase 3: Background Calculation Job

### Status
Proposed

### Performance Metrics

**Historical Data Backfill Performance:**
- Estimated completion time: 8 hour per 1TB of existing data on 32vCores
- Processing occurs in batches to minimize performance impact during schema upgrade

**Background Calculation Job Performance:**
- Small database (3TB): Approximately 2 minutes per calculation cycle
- Large database (128TB): Approximately 4 hours per calculation cycle
- Job frequency: Runs every 4 hours to maintain current metrics
- Database size correlation: Calculation time scales linearly with database size

### Consequences
- Background job adds periodic database load every 4 hours
- Failure in job does not impact core FHIR server functionality
- Falure in job results in stale data size metrics until next successful run

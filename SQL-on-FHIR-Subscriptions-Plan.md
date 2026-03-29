# SQL on FHIR with Subscription-Based Refreshes

## Project Overview
Combine two complementary FHIR specifications—**SQL on FHIR v2 ViewDefinitions** and **FHIR R4 Subscriptions**—to create an event-driven system where materialized analytic views stay current as clinical data changes in real-time. This transforms traditional batch ETL pipelines into reactive data flows.

**Target Presentation:** DevDays 2026  
**Branch:** `feature/subscription-sqlonfhir` (based on `feature/subscription-engine`, merged with latest `main`)

---

## Status Tracker

| # | Phase | Task | Status |
|---|-------|------|--------|
| 1 | Foundation | Rebase feature branch onto main | ✅ Done |
| 2 | Foundation | Add Ignixa NuGet packages | ✅ Done |
| 3 | Foundation | IElement adapter layer | ✅ Done |
| 4 | Foundation | Ignixa integration smoke test | ✅ Done |
| 5 | Materialization | SQL Table Schema Manager (`sqlfhir` schema) | ✅ Done |
| 6 | Materialization | Incremental row updater | ✅ Done |
| 7 | Materialization | Full population background job | ✅ Done |
| 8 | Materialization | Materialization integration tests | ✅ Done |
| 9 | Subscription | ViewDefinition Refresh Channel | ✅ Done |
| 10 | Subscription | Auto-subscription registration | ✅ Done |
| 11 | Subscription | End-to-end flow test | ✅ Done |
| 12 | Multi-Target | Parquet materializer for Fabric | ✅ Done |
| 13 | API | `$viewdefinition-run` operation | ✅ Done |
| 14 | API | Materialization status tracking | ✅ Done |
| 15 | Docs | Documentation and ADR | ✅ Done |

---

## Background Research

### SQL on FHIR v2 (spec: build.fhir.org/ig/FHIR/sql-on-fhir-v2/)
- **ViewDefinition**: A portable JSON format for defining tabular projections of FHIR data. Each targets a single resource type and uses FHIRPath expressions for columns, filters (`where`), and unnesting (`forEach`, `forEachOrNull`, `repeat`).
- **SQLQuery**: A FHIR Library profile for shareable SQL queries that join/aggregate materialized ViewDefinition tables.
- **HTTP API**: `$viewdefinition-run` (sync), `$viewdefinition-export` (async bulk), `$sqlquery-run`, `$sqlquery-export`.
- **Key constraint**: A single ViewDefinition targets exactly one resource type. Cross-resource joins happen downstream in the analytics layer.
- **View Runners**: "In-memory" (ETL-style, resource→rows→output) vs "In-database" (translate ViewDefinition to SQL over FHIR-native schema). We'll likely need an in-memory runner for incremental updates.

### Existing Subscription Engine (feature/subscription-engine branch)
- **SubscriptionManager**: Caches active subscriptions in memory, syncs from FHIR store.
- **SubscriptionsOrchestratorJob**: Triggered per transaction, evaluates subscription filter criteria using in-memory search indexing (reuses existing `SearchIndexer` + `SearchQueryInterpreter`).
- **SubscriptionProcessingJob**: Delivers notifications via pluggable channels.
- **Channels**: RestHook, Storage (Azure Blob), DataLake (NDJSON to ADLS).
- **Filter matching**: Parses criteria like `Patient?name=John`, builds expression tree, evaluates against in-memory index of transaction resources.
- **Validation pipeline**: `CreateOrUpdateSubscriptionBehavior` validates and activates subscriptions via handshake.
- **Heartbeat**: Background service sends periodic heartbeats.
- **Status**: Functional but limited test coverage. R4-only (uses backport profile).

---

## ViewDefinition Runner: Ignixa — We Have One!

### 🎉 The Ignixa FHIR Project Already Has Everything We Need

[**Ignixa FHIR**](https://github.com/brendankowitz/ignixa-fhir) (MIT license) is a modular .NET FHIR ecosystem that includes three NuGet packages that solve our biggest problems:

#### 1. `Ignixa.FhirPath` — Fast, Compiled FHIRPath Engine
- **Compile-time optimizations**: Constant folding, short-circuiting, algebraic simplification
- **Expression caching**: Compiled expressions cached for repeated use
- **Compiled delegate mode**: 80% faster for common patterns (simple paths, where clauses, first())
- **Custom function registration**: Extend with `getResourceKey()`, `getReferenceKey()` via subclass
- **Works with `IElement` abstraction**: Not tied to Firely models
- **NuGet**: `dotnet add package Ignixa.FhirPath`

#### 2. `Ignixa.SqlOnFhir` — Complete ViewDefinition Runner
Already implements the SQL on FHIR v2 spec! Key classes:
- **`ViewDefinition`** model: `Resource`, `Select`, `Where`, `Constant` — all parsed
- **`SelectGroup`**: `Column`, `ForEach`, `ForEachOrNull` — unnesting logic built in
- **`ViewColumnDefinition`**: `Name`, `Path`, `Type` — column definitions
- **`WhereClause`**: FHIRPath boolean filters
- **`ViewConstant`**: Parameterized constants
- **`SqlOnFhirEvaluator`**: Core evaluator — takes a ViewDefinition + resources → produces rows
- **NuGet**: `dotnet add package Ignixa.SqlOnFhir`

Usage is exactly what we need:
```csharp
var evaluator = new SqlOnFhirEvaluator(schema);
var rows = evaluator.Evaluate(viewDefinition, resources);
// Each row is a dictionary of column_name → value
```

#### 3. `Ignixa.SqlOnFhir.Writers` — Output Writers (CSV + Parquet!)
- **`CsvFileWriter`**: Write ViewDefinition results to CSV
- **`ParquetFileWriter`**: Write ViewDefinition results to Parquet files
- Perfect for Fabric/OneLake/ADLS materialization targets
- **NuGet**: `dotnet add package Ignixa.SqlOnFhir.Writers`

### What This Means for Our Plan
Instead of building a ViewDefinition runner from scratch (the hardest part), we:
1. **Reference Ignixa NuGet packages** for the runner + FHIRPath engine
2. **Build only the integration layer**: SQL Server materializer + subscription channel
3. **Get Parquet output for free** via `Ignixa.SqlOnFhir.Writers`

This cuts Phase 1 from "build a FHIRPath engine and runner" to "integrate existing NuGet packages."

### Compatibility Considerations
- Ignixa uses `IElement` abstraction, not Firely's `Base` model. We'll need an adapter between the FHIR server's resource model and Ignixa's `IElement` interface.
- Ignixa targets .NET 9.0 (same as our FHIR server's global.json SDK version)
- MIT licensed — compatible with our project

---

## Example ViewDefinitions and Incremental Update Benefits

### Blood Pressure View — Best Demo for Incremental Updates
The `UsCoreBloodPressures` ViewDefinition is the ideal demo example:

```json
{
  "resource": "Observation",
  "name": "us_core_blood_pressures",
  "constant": [
    {"name": "systolic_bp", "valueCode": "8480-6"},
    {"name": "diastolic_bp", "valueCode": "8462-4"},
    {"name": "bp_code", "valueCode": "85354-9"}
  ],
  "select": [
    {"column": [
      {"path": "getResourceKey()", "name": "id"},
      {"path": "subject.getReferenceKey(Patient)", "name": "patient_id"},
      {"path": "effective.ofType(dateTime)", "name": "effective_date_time"}
    ]},
    {"forEach": "component.where(code.coding.exists(system='http://loinc.org' and code=%systolic_bp)).first()",
     "column": [
       {"path": "value.ofType(Quantity).value", "name": "sbp_quantity_value"}
    ]},
    {"forEach": "component.where(code.coding.exists(system='http://loinc.org' and code=%diastolic_bp)).first()",
     "column": [
       {"path": "value.ofType(Quantity).value", "name": "dbp_quantity_value"}
    ]}
  ],
  "where": [{"path": "code.coding.exists(system='http://loinc.org' and code=%bp_code)"}]
}
```

**Why this highlights incremental updates:**
- A hospital records **thousands of BP Observations per day**
- **Batch ETL**: Re-process ALL Observations nightly → hours of compute, 24h stale data
- **Subscription-driven**: New BP recorded → subscription fires → runner evaluates that ONE Observation → one row inserted into `us_core_blood_pressures` table → **sub-second freshness**
- The `where` filter means non-BP observations are ignored by the subscription (no wasted work)
- When a BP is corrected (updated), only that row is replaced

### Condition View — Demonstrates Status Change Updates
The `ConditionFlat` ViewDefinition shows `forEachOrNull` with coding arrays:
- When a condition's `clinicalStatus` changes from `active` → `resolved`, the subscription fires
- The runner re-evaluates that Condition → updates the `clinical_status` column in the materialized table
- Downstream queries (e.g., "all active diabetics") immediately reflect the change

### Contrast: Batch ETL vs Event-Driven

| Aspect | Batch ETL | Subscription-Driven |
|--------|-----------|-------------------|
| Data freshness | 24h (nightly) | Sub-second |
| Compute cost | Full re-scan of all resources | Only changed resources |
| Complexity | Custom pipeline per view | Standard ViewDefinition + auto-subscription |
| Failure blast radius | Entire pipeline re-run | Retry single resource |
| Adding a new view | Build new ETL pipeline | POST a ViewDefinition JSON |

---

## Materialization Targets: Beyond SQL Server

### SQL Server (Primary Target)
- **Pros**: Already the FHIR server's data store; enables joins with FHIR data; no external dependencies; low latency
- **Use case**: Real-time operational analytics, CDS, quality dashboards
- **Schema**: `sqlfhir.*` schema in the same database

### Microsoft Fabric / OneLake (Strategic Target)
Fabric is **the natural next step** and a compelling DevDays demo angle:
- The existing subscription engine already has a **DataLake channel** that writes NDJSON to Azure Data Lake Storage (ADLS)
- Fabric's Lakehouse sits directly on OneLake (which is ADLS Gen2 under the hood)
- **Approach**: A "Fabric Channel" writes Parquet files (not NDJSON) organized by ViewDefinition name
- Fabric auto-discovers Parquet in OneLake → tables appear in the SQL Analytics Endpoint
- Power BI, Spark notebooks, and SQL all work immediately
- **Incremental benefit**: Append new Parquet files per subscription event; Fabric handles compaction
- **Demo**: Show a Power BI dashboard over a Fabric Lakehouse that updates as FHIR resources change

### Parquet Files (Portable Output)
- The SQL on FHIR spec's `$viewdefinition-export` operation explicitly supports Parquet as an output format
- Parquet is columnar, compressed, and the lingua franca of analytics tools
- **Use case**: Research data exports, bulk analytics, ML training datasets
- Works with: Spark, Databricks, BigQuery, Snowflake, DuckDB, Pandas

### Channel Architecture for Multiple Targets

```
Subscription Event
       │
       ▼
┌──────────────────┐
│ ViewDefinition    │
│ Refresh Channel   │
│                   │
│  ┌─────────────┐  │
│  │ Runner      │  │  (evaluates ViewDef → rows)
│  └──────┬──────┘  │
│         │         │
│  ┌──────▼──────┐  │
│  │ Materializer│  │  (pluggable output target)
│  │  Interface  │  │
│  └──────┬──────┘  │
└─────────┼─────────┘
          │
    ┌─────┼─────────┬──────────────┐
    ▼     ▼         ▼              ▼
┌──────┐ ┌───────┐ ┌────────────┐ ┌──────────┐
│ SQL  │ │Parquet│ │ Fabric/    │ │ Future:  │
│Server│ │ File  │ │ OneLake    │ │ Snowflake│
│      │ │       │ │            │ │ BigQuery │
└──────┘ └───────┘ └────────────┘ └──────────┘
```

This makes the ViewDefinition Refresh Channel a two-part design:
1. **Runner** (spec-standard): ViewDefinition → rows (shared across all targets)
2. **Materializer** (pluggable): rows → target-specific storage (SQL INSERT, Parquet write, API call)

---

## Critique of Initial Approach

### The Proposal
> When a ViewDefinition is submitted, a FHIR query populates the data into a SQL table. The query is registered as a subscription, and subscription triggers update the materialized view.

### Strengths ✅
1. **Elegant spec synergy** — Uses two standard FHIR specs together, each doing what it's designed for.
2. **Event-driven > batch** — Eliminates polling/scheduling; views update as data changes.
3. **Natural mapping** — ViewDefinition's `resource` field maps directly to subscription resource type filtering.
4. **Existing infrastructure** — The subscription engine already does in-memory search filtering and has pluggable notification channels—a "ViewDefinition refresh" channel is a natural extension.
5. **SQL Server is the right target** — The FHIR server already uses SQL Server; materialized views alongside FHIR data enables powerful joins.

### Concerns & Gaps ⚠️

#### 1. Semantic Gap Between ViewDefinition `where` and Subscription Criteria
- ViewDefinition `where` clauses use **FHIRPath** (e.g., `code.coding.exists(system='http://loinc.org' and code='8480-6')`)
- Subscription criteria use **FHIR search parameters** (e.g., `Observation?code=http://loinc.org|8480-6`)
- **Risk**: Not all FHIRPath filters can be expressed as search parameters. The auto-generated subscription may be broader than the ViewDefinition filter, causing unnecessary refreshes (but not correctness issues—just efficiency).
- **Mitigation**: Use a "best-effort" subscription filter (match on resource type + key search params), then re-evaluate FHIRPath `where` during materialization to filter false positives.

#### 2. Incremental vs Full Refresh Granularity
- The proposal implies a full re-query on each subscription event. This doesn't scale.
- **Better**: Incremental upsert—when a resource changes, re-evaluate the ViewDefinition for *just that resource* and upsert/delete its rows in the materialized table.
- The subscription notification already includes the changed resource(s), so we have exactly what we need.

#### 3. Multi-Row Output from Single Resources
- ViewDefinitions with `forEach`/`forEachOrNull`/`repeat` can produce **multiple rows** from a single resource (e.g., a Patient with 3 addresses → 3 rows in `patient_addresses`).
- **Challenge**: Incremental update must delete all existing rows for a resource before inserting new ones (not a simple upsert by resource ID alone).
- **Solution**: Use a composite key of `(resource_key, row_index)` or simply `DELETE WHERE resource_key = X` then re-insert all rows for that resource.

#### 4. Handling Deletes
- If a resource is deleted, or is updated so it no longer matches the `where` filter, rows must be removed.
- The subscription engine fires on creates, updates, *and* deletes—so we have the signal. On delete: remove all rows for that resource. On update: re-evaluate and if zero rows result, effectively a delete.

#### 5. Initial Population
- When a ViewDefinition is first submitted (or the server restarts), the materialized table needs to be fully populated from existing data before incremental mode can begin.
- **Solution**: `$viewdefinition-run` or a background job does the initial full scan. The subscription kicks in for subsequent changes. Need a state machine: `Creating → Populating → Active → Error`.

#### 6. Schema Management
- ViewDefinition columns define the table schema. What happens when a ViewDefinition is updated with new columns?
- **Solution**: Schema evolution—add new columns (nullable), or drop-and-recreate. Flag to user that schema changes may require re-population.

#### 7. Performance Under High Write Volume
- High-throughput FHIR servers may see thousands of writes/second. Each triggering a ViewDefinition re-evaluation could be expensive.
- **Mitigation**: Batch incremental updates. The orchestrator job already batches by `MaxCount`. Group multiple resource changes and apply them in a single SQL transaction.

---

## Refined Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                      FHIR Server (R4)                           │
│                                                                 │
│  ┌──────────────┐    ┌──────────────────┐    ┌───────────────┐  │
│  │ FHIR REST API │───▶│  MediatR Pipeline │───▶│  Data Store   │  │
│  └──────────────┘    └────────┬─────────┘    └───────────────┘  │
│                               │                                  │
│                    ┌──────────▼──────────┐                       │
│                    │ Subscription Engine  │                       │
│                    │  (Orchestrator Job)  │                       │
│                    └──────────┬──────────┘                       │
│                               │                                  │
│              ┌────────────────┼────────────────┐                 │
│              ▼                ▼                ▼                  │
│     ┌──────────────┐ ┌──────────────┐ ┌──────────────────┐      │
│     │  RestHook     │ │  DataLake    │ │ ViewDefinition   │      │
│     │  Channel      │ │  Channel     │ │ Refresh Channel  │      │
│     └──────────────┘ └──────────────┘ │    (NEW)          │      │
│                                        └────────┬─────────┘      │
│                                                 │                │
│                                       ┌─────────▼──────────┐    │
│                                       │ ViewDefinition      │    │
│                                       │ Runner (In-Memory)  │    │
│                                       │  - FHIRPath eval    │    │
│                                       │  - Column mapping   │    │
│                                       │  - Row generation   │    │
│                                       └─────────┬──────────┘    │
│                                                 │                │
│                                       ┌─────────▼──────────┐    │
│                                       │ Materialization     │    │
│                                       │ Layer               │    │
│                                       │  - SQL DDL mgmt     │    │
│                                       │  - Incremental      │    │
│                                       │    upsert/delete    │    │
│                                       │  - Full refresh     │    │
│                                       └─────────┬──────────┘    │
│                                                 │                │
│                                       ┌─────────▼──────────┐    │
│                                       │ SQL Server          │    │
│                                       │ (Materialized       │    │
│                                       │  View Tables)       │    │
│                                       └────────────────────┘    │
└─────────────────────────────────────────────────────────────────┘
```

### Key New Components to Build

#### 1. ViewDefinition Resource Support
- Parse/validate ViewDefinition JSON submitted to the server
- Store ViewDefinitions as FHIR resources (custom resource or use Binary/Basic)
- API: `POST /ViewDefinition`, `GET /ViewDefinition/{id}`, `$viewdefinition-run`

#### 2. ViewDefinition Runner (In-Memory)
- Evaluate FHIRPath expressions from ViewDefinition `select` against a FHIR resource
- Handle `forEach`, `forEachOrNull`, `repeat`, `unionAll`
- Map FHIRPath results to typed columns
- Support `getResourceKey()` and `getReferenceKey()` functions
- Output: `IEnumerable<Dictionary<string, object>>` (rows of column name → value)

#### 3. ViewDefinition Materialization Layer
- **Schema Manager**: Translate ViewDefinition columns → SQL DDL (`CREATE TABLE`)
  - Column type mapping: FHIR types → SQL types (string→nvarchar, dateTime→datetime2, etc.)
  - Include `_resource_key` column for incremental update tracking
- **Incremental Updater**: 
  - `DELETE FROM [view_table] WHERE _resource_key = @resourceKey`
  - Re-run ViewDefinition for single resource
  - `INSERT` new rows
- **Full Populator**: Background job that runs ViewDefinition against all matching resources
- **Table naming**: `sqlfhir_[viewdefinition_name]` (namespaced to avoid conflicts)

#### 4. ViewDefinition Refresh Channel (New Subscription Channel)
- Implements `ISubscriptionChannel`
- On notification: receives changed resource(s) + subscription info
- Looks up associated ViewDefinition(s)
- Runs ViewDefinition against changed resources
- Applies incremental updates to materialized table
- Channel type: `"view-refresh"` (custom)

#### 5. Auto-Subscription Registration
- When a ViewDefinition is submitted and materialization is requested:
  1. Create/update the SQL table schema
  2. Kick off full population job
  3. **`ViewDefinitionSubscriptionManager`** generates N Subscription resources:
     - At minimum: one broad subscription with `criteria`: `{ViewDefinition.resource}?`
     - Optionally: narrower subscriptions with search param equivalents of `where` clauses
     - All subscriptions share:
       - `channel.type`: `view-refresh`
       - `channel.endpoint`: internal reference to the ViewDefinition
     - The manager tracks the 1:N relationship (ViewDefinition → Subscriptions)
  4. Subscription engine handles the rest
  5. On ViewDefinition removal, the manager deletes all associated Subscriptions

---

## Implementation Todos (Revised with Ignixa)

### Phase 1: Foundation & Integration
1. **Rebase feature branch** — Get `feature/subscription-engine` up to date with `main`
2. **Add Ignixa NuGet packages** — Reference `Ignixa.SqlOnFhir`, `Ignixa.FhirPath`, `Ignixa.SqlOnFhir.Writers`
3. **IElement adapter** — Bridge between FHIR server's resource model and Ignixa's `IElement` interface
4. **Smoke test** — Evaluate a PatientDemographics ViewDefinition against a Patient resource using Ignixa

### Phase 2: SQL Server Materialization
5. **SQL Table Schema Manager** — Translate ViewDefinition columns → CREATE TABLE DDL in `sqlfhir` schema
6. **Incremental Updater** — Delete-then-insert for a single resource's rows
7. **Full Population Job** — Background job: scan all resources of type, run ViewDefinition via Ignixa, bulk insert
8. **Materialization integration tests**

### Phase 3: Subscription Integration
9. **ViewDefinition Refresh Channel** — New `ISubscriptionChannel` implementation using Ignixa evaluator
10. **Auto-subscription registration** — On ViewDefinition Library submit, auto-create Subscription
11. **End-to-end flow** — Submit ViewDefinition → table created → data populated → resource CRUD → table updated
12. **E2E tests**

### Phase 4: Multi-Target & API
13. **Parquet materializer** — Use `Ignixa.SqlOnFhir.Writers.ParquetFileWriter` for Fabric/ADLS output
14. **$viewdefinition-run operation** — Sync evaluation endpoint per spec
15. **Status tracking** — ViewDefinition materialization state (Creating/Populating/Active/Error)
16. **Documentation & ADR**

---

## DevDays Demo Scenarios

### Demo 1: "Hello World" — Patient Demographics View (5 min)
1. Show the ViewDefinition JSON for `patient_demographics` (from the spec example)
2. POST it to the FHIR server with `?materialize=true`
3. Show the auto-created SQL table with existing patients
4. Show the auto-created Subscription
5. Create a new Patient via FHIR API
6. Query the SQL table — new patient appears within seconds
7. **Takeaway**: Zero-config analytics table that stays current

### Demo 2: Blood Pressure Monitoring — Incremental Updates in Action (10 min)
**Scenario: ICU Blood Pressure Tracking**

An ICU needs real-time BP trends across all patients. Today this requires custom integrations.

1. Show the `UsCoreBloodPressures` ViewDefinition (from the spec — uses constants, forEach, where filter)
2. Materialize it → SQL table `sqlfhir.us_core_blood_pressures` auto-created
3. Show existing data: `SELECT patient_id, effective_date_time, sbp_quantity_value, dbp_quantity_value FROM sqlfhir.us_core_blood_pressures`
4. Post a new BP Observation (systolic=145, diastolic=95) via FHIR API
5. Query the table again — **new row appears in sub-seconds**
6. Post a non-BP Observation (e.g., heart rate) — table is unchanged (subscription filter ignores it)
7. Show the before/after comparison:
   - **Batch**: Re-scan 500K Observations nightly, rebuild entire table → hours, stale
   - **Subscription**: Process 1 Observation → 1 row insert → milliseconds, fresh
8. **Takeaway**: Only changed resources are processed; irrelevant resources are filtered out

### Demo 3: Fabric Lakehouse — From FHIR to Power BI (10 min)
**Scenario: Population Health Dashboard**

1. Create two ViewDefinitions: `patient_demographics` + `condition_flat`
2. Materialize to **Fabric OneLake** (via Parquet materializer channel)
3. Show Parquet files appearing in Fabric Lakehouse
4. Open SQL Analytics Endpoint — tables auto-discovered
5. Open Power BI dashboard showing patient demographics + condition distribution
6. Create a new Patient with a diabetes Condition via FHIR API
7. Dashboard updates automatically (Fabric picks up new Parquet file)
8. **Takeaway**: FHIR server → Fabric → Power BI with zero custom ETL pipeline

### Demo 4: Architecture Deep-Dive (5 min)
1. Walk through the subscription engine flow with diagrams
2. Show the pluggable Runner + Materializer architecture
3. Show the incremental update path (delete old rows → re-evaluate → insert new rows)
4. Show how adding a new output target (Fabric, Snowflake) is just a new Materializer implementation
5. **Takeaway**: Clean, extensible architecture leveraging existing FHIR specs

---

## Real-World Scenario: Why This Matters

### Problem: Quality Measure Reporting Lag
- Hospitals submit quality measures (eCQMs) to CMS quarterly
- Current workflow: Nightly ETL extracts FHIR data → transforms → loads into analytics DB
- Pain points:
  - **Staleness**: Data is always 24+ hours old
  - **Complexity**: Custom ETL pipelines for each measure
  - **Brittleness**: Schema changes break pipelines
  - **Cost**: Full re-extraction even for small changes

### Solution: Subscription-Driven ViewDefinitions
- Define quality measure data needs as ViewDefinitions (standardized, portable)
- Materialized views update in real-time as clinical data changes
- Quality dashboards always show current data
- Adding a new measure = adding a ViewDefinition (no ETL pipeline to build)

### Other High-Value Use Cases
1. **Clinical Decision Support**: Real-time views of patient medications, allergies, conditions for CDS rules
2. **Population Health Management**: Materialized views of chronic disease cohorts, updated as diagnoses change
3. **Research Cohort Discovery**: Views filtering patients by inclusion/exclusion criteria, always current
4. **Operational Analytics**: Views of appointments, encounters, wait times for operational dashboards
5. **Public Health Reporting**: Syndromic surveillance views that update as new encounters arrive

---

## Design Decisions (Resolved)

1. **ViewDefinition as a FHIR resource type** → **Library resource with ViewDefinition extension**
   - Store as a `Library` resource with a profile/extension containing the ViewDefinition JSON
   - Enables standard FHIR CRUD, search, versioning

2. **External vs Internal SQL tables** → **Same database, `sqlfhir` schema**
   - Materialized views live in `sqlfhir.*` schema in the FHIR SQL Server database
   - Enables joins with FHIR data while keeping concerns separated

3. **FHIRPath engine** → **Ignixa.FhirPath** (see details below)
   - Use the [Ignixa FHIR](https://github.com/brendankowitz/ignixa-fhir) compiled FHIRPath engine
   - This also gives us the complete **Ignixa.SqlOnFhir** ViewDefinition runner for free

4. **Concurrency during full population** → **Queue incoming subscription events**
   - Events arriving during initial population are queued
   - Applied after full population completes using a watermark timestamp

5. **Multi-tenancy** → **Not in scope**
   - Single-tenant only for this implementation

6. **Spec contribution** → **Yes, write up after implementation works**
   - Document how subscription-based refresh could be incorporated into the SQL on FHIR spec

7. **ViewDefinition-to-Subscription cardinality** → **1:N (one ViewDefinition, many Subscriptions)**
   - A single FHIR Subscription supports only **one criteria string** (e.g., `Observation?code=http://loinc.org|85354-9`). It cannot express multiple independent filter queries.
   - A ViewDefinition's `where` clauses use **FHIRPath**, which may not map cleanly to a single FHIR search query — or may require multiple search queries for full coverage.
   - **Design**: One ViewDefinition can produce N Subscriptions, all pointing to the same **ViewDefinition Refresh Channel**:
     ```
     ViewDefinition (1) ──► (N) Subscriptions ──► (1) ViewDefinition Refresh Channel
     ```
   - A **`ViewDefinitionSubscriptionManager`** owns the lifecycle: when a ViewDefinition is registered for materialization, it generates the appropriate subscription(s); when removed, it cleans them up.
   - **Safe default**: At minimum, one broad subscription per resource type (`Observation?`) guarantees no missed updates. Narrower criteria are a **pure optimization** — the ViewDefinition evaluator always re-applies FHIRPath `where` clauses, so over-triggering is safe (just less efficient).
   - **Criteria generation strategy** (phased):
     - **Phase 1**: Resource-type-only (`Observation?`) — simple, correct, no missed updates.
     - **Phase 2**: Pattern-match common FHIRPath idioms to search params (e.g., `code.coding.exists(system='X' and code='Y')` → `?code=X|Y`). Multiple patterns may produce multiple subscriptions for the same ViewDefinition.
     - **Future**: Reverse-match against FHIR search parameter FHIRPath definitions for broader coverage.
   - **Correctness guarantee**: The evaluator's FHIRPath `where` filtering is the single source of truth. Subscription criteria only control *when* the evaluator runs — a broader subscription means more evaluator invocations (cost), but never incorrect results.

---

## Branch Strategy
- Start from `feature/subscription-engine`
- Rebase onto latest `main`
- Create new branch: `feature/sql-on-fhir-subscriptions`
- Work in phases, PR each phase back to the feature branch
- Eventually PR the complete feature to `main`

---

## References
- [SQL on FHIR v2 Spec](https://build.fhir.org/ig/FHIR/sql-on-fhir-v2/)
- [ViewDefinition Structure](https://build.fhir.org/ig/FHIR/sql-on-fhir-v2/StructureDefinition-ViewDefinition.html)
- [SQL on FHIR HTTP API](https://build.fhir.org/ig/FHIR/sql-on-fhir-v2/operations.html)
- [FHIR Subscriptions R5](https://hl7.org/fhir/R5/subscription.html)
- [FHIR Subscriptions Backport IG](http://hl7.org/fhir/uv/subscriptions-backport/)
- [feature/subscription-engine branch](https://github.com/microsoft/fhir-server/tree/feature/subscription-engine)
- [Ignixa FHIR Server](https://github.com/brendankowitz/ignixa-fhir) — Source of FHIRPath engine, SQL on FHIR runner, and Parquet writer
  - [Ignixa.FhirPath README](https://github.com/brendankowitz/ignixa-fhir/blob/main/src/Core/Ignixa.FhirPath/README.md)
  - [Ignixa.SqlOnFhir README](https://github.com/brendankowitz/ignixa-fhir/blob/main/src/Core/Ignixa.SqlOnFhir/README.md)
  - [Ignixa.SqlOnFhir.Writers](https://github.com/brendankowitz/ignixa-fhir/tree/main/src/Core/Ignixa.SqlOnFhir.Writers) (CsvFileWriter, ParquetFileWriter)
- [SQL on FHIR Reference Tests](https://github.com/FHIR/sql-on-fhir-v2/tree/master/tests) — 20+ JSON conformance test fixtures
- [SQL on FHIR JS Reference Runner](https://github.com/FHIR/sql-on-fhir-v2/tree/master/sof-js) — `sof-js` reference implementation

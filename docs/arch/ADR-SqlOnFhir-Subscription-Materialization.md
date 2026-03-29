# ADR: SQL on FHIR v2 with Subscription-Driven Materialization

## Status
Accepted

## Date
2026-03-29

## Context
Healthcare analytics pipelines typically rely on batch ETL processes to transform FHIR data into
tabular formats for reporting, dashboards, and analytics tools. This introduces 24+ hour data
staleness, custom pipeline complexity per report, and high compute costs from full re-extraction.

The SQL on FHIR v2 specification defines ViewDefinitions — portable JSON structures that project
FHIR resources into tabular schemas using FHIRPath expressions. Combined with the FHIR Subscriptions
framework, we can create event-driven materialized views that update in real-time as clinical data
changes, eliminating batch ETL entirely.

## Decision
Implement a materialization layer in the Microsoft FHIR Server that:
1. Accepts ViewDefinition resources for registration
2. Creates and populates SQL tables in a dedicated `sqlfhir` schema
3. Auto-creates FHIR Subscriptions to receive change notifications
4. Incrementally updates materialized rows via a new ViewDefinition Refresh subscription channel
5. Supports multiple output targets (SQL Server, Parquet/Fabric)
6. Exposes spec-standard `$viewdefinition-run` and `$viewdefinition-export` operations

## Architecture

### Component Diagram

```mermaid
graph TB
    subgraph "FHIR Server"
        subgraph "API Layer"
            CTRL["ViewDefinitionRunController<br/>$run / $viewdefinition-export"]
            FHIR_API["FHIR REST API<br/>POST/PUT/DELETE Resources"]
        end

        subgraph "MediatR Pipeline"
            MEDIATOR["IMediator"]
            RUN_HANDLER["ViewDefinitionRunHandler<br/>Inline eval or materialized read"]
            EXPORT_HANDLER["ViewDefinitionExportHandler<br/>Fast-path or async job"]
            SUB_BEHAVIOR["CreateOrUpdateSubscriptionBehavior<br/>Validates & activates subscriptions"]
            CREATE_HANDLER["CreateResourceHandler<br/>Persists resources"]
        end

        subgraph "SQL on FHIR Module (Microsoft.Health.Fhir.SqlOnFhir)"
            subgraph "Channels"
                VD_SUB_MGR["ViewDefinitionSubscriptionManager<br/>Registers ViewDefs, creates Subscriptions,<br/>tracks 1:N mapping & lifecycle status"]
                VD_REFRESH["ViewDefinitionRefreshChannel<br/>ISubscriptionChannel implementation<br/>Routes change events to materializer"]
            end

            subgraph "Materialization"
                SCHEMA_MGR["SqlServerViewDefinitionSchemaManager<br/>CREATE TABLE DDL in sqlfhir schema"]
                SQL_MAT["SqlServerViewDefinitionMaterializer<br/>DELETE + INSERT atomic upserts"]
                PARQUET_MAT["ParquetViewDefinitionMaterializer<br/>Parquet files to Azure Blob/ADLS"]
                MAT_FACTORY["MaterializerFactory<br/>Routes to SQL, Parquet, or both"]
                TYPE_MAP["FhirTypeToSqlTypeMap<br/>FHIR types → SQL Server types"]
            end

            subgraph "Background Jobs"
                POP_ORCH["PopulationOrchestratorJob<br/>Creates table, enqueues processing"]
                POP_PROC["PopulationProcessingJob<br/>Batch search → evaluate → materialize"]
            end

            subgraph "Ignixa Integration"
                EVALUATOR["ViewDefinitionEvaluator<br/>Bridges Firely SDK ↔ Ignixa IElement"]
            end
        end

        subgraph "Ignixa NuGet Packages (External)"
            IGNIXA_EVAL["Ignixa.SqlOnFhir<br/>SqlOnFhirEvaluator<br/>SqlOnFhirSchemaEvaluator"]
            IGNIXA_FP["Ignixa.FhirPath<br/>Compiled FHIRPath engine"]
            IGNIXA_WRITE["Ignixa.SqlOnFhir.Writers<br/>ParquetFileWriter / CsvFileWriter"]
        end

        subgraph "Subscription Engine (Existing)"
            SUB_ORCH["SubscriptionsOrchestratorJob<br/>Detects resource changes per transaction"]
            SUB_PROC["SubscriptionProcessingJob<br/>Resolves channel, calls PublishAsync"]
            SUB_MGR["SubscriptionManager<br/>Caches active subscriptions"]
            CHAN_FACTORY["SubscriptionChannelFactory<br/>Maps channel type → implementation"]
        end

        subgraph "Data Layer"
            SQL_DB[("SQL Server<br/>dbo.Resource (FHIR data)<br/>sqlfhir.* (materialized views)")]
            QUEUE[("Job Queue<br/>dbo.JobQueue")]
        end

        subgraph "External Storage"
            BLOB[("Azure Blob / ADLS / OneLake<br/>Parquet files")]
        end
    end

    %% API flows
    CTRL --> MEDIATOR
    FHIR_API --> MEDIATOR
    MEDIATOR --> RUN_HANDLER
    MEDIATOR --> EXPORT_HANDLER
    MEDIATOR --> SUB_BEHAVIOR --> CREATE_HANDLER

    %% Registration flow
    VD_SUB_MGR --> SCHEMA_MGR
    VD_SUB_MGR --> MEDIATOR
    VD_SUB_MGR --> QUEUE

    %% Evaluation
    EVALUATOR --> IGNIXA_EVAL
    IGNIXA_EVAL --> IGNIXA_FP
    RUN_HANDLER --> EVALUATOR
    RUN_HANDLER --> SQL_DB

    %% Materialization
    SQL_MAT --> SQL_DB
    PARQUET_MAT --> IGNIXA_WRITE
    PARQUET_MAT --> BLOB
    MAT_FACTORY --> SQL_MAT
    MAT_FACTORY --> PARQUET_MAT
    SCHEMA_MGR --> SQL_DB
    SCHEMA_MGR --> IGNIXA_EVAL
    TYPE_MAP -.-> SCHEMA_MGR

    %% Subscription flow
    CREATE_HANDLER --> SQL_DB
    SQL_DB --> SUB_ORCH
    SUB_ORCH --> SUB_MGR
    SUB_ORCH --> QUEUE
    QUEUE --> SUB_PROC
    SUB_PROC --> CHAN_FACTORY
    CHAN_FACTORY --> VD_REFRESH
    VD_REFRESH --> EVALUATOR
    VD_REFRESH --> MAT_FACTORY

    %% Background jobs
    QUEUE --> POP_ORCH
    POP_ORCH --> SCHEMA_MGR
    POP_ORCH --> QUEUE
    QUEUE --> POP_PROC
    POP_PROC --> EVALUATOR
    POP_PROC --> MAT_FACTORY

    %% Styling
    classDef ignixa fill:#e1f5fe,stroke:#0288d1
    classDef existing fill:#f3e5f5,stroke:#7b1fa2
    classDef new fill:#e8f5e9,stroke:#2e7d32
    classDef storage fill:#fff3e0,stroke:#ef6c00

    class IGNIXA_EVAL,IGNIXA_FP,IGNIXA_WRITE ignixa
    class SUB_ORCH,SUB_PROC,SUB_MGR,CHAN_FACTORY,SUB_BEHAVIOR,CREATE_HANDLER,FHIR_API existing
    class VD_SUB_MGR,VD_REFRESH,SCHEMA_MGR,SQL_MAT,PARQUET_MAT,MAT_FACTORY,TYPE_MAP,POP_ORCH,POP_PROC,EVALUATOR,CTRL,RUN_HANDLER,EXPORT_HANDLER new
    class SQL_DB,QUEUE,BLOB storage
```

### Sequence Diagram: Full Lifecycle

```mermaid
sequenceDiagram
    actor User
    participant API as FHIR API
    participant SubMgr as ViewDefinition<br/>SubscriptionManager
    participant SchemaMgr as Schema Manager
    participant SQL as SQL Server<br/>(sqlfhir schema)
    participant Queue as Job Queue
    participant PopJob as Population Job
    participant SearchSvc as Search Service
    participant Evaluator as ViewDefinition<br/>Evaluator (Ignixa)
    participant Materializer as Materializer
    participant SubEngine as Subscription Engine
    participant RefreshChan as ViewDefinition<br/>Refresh Channel

    Note over User,RefreshChan: Phase 1: ViewDefinition Registration

    User->>API: POST ViewDefinition<br/>(register for materialization)
    API->>SubMgr: RegisterAsync(viewDefJson)

    SubMgr->>SchemaMgr: CreateTableAsync(viewDefJson)
    SchemaMgr->>Evaluator: GetColumnDefinitions()
    Evaluator-->>SchemaMgr: [_resource_key, id, gender, ...]
    SchemaMgr->>SQL: CREATE TABLE sqlfhir.patient_demographics
    SQL-->>SchemaMgr: ✓ Table created

    SubMgr->>Queue: Enqueue PopulationOrchestratorJob
    Queue-->>SubMgr: ✓ Job queued

    SubMgr->>API: mediator.Send(CreateResourceRequest<br/>{Subscription: Patient?, view-refresh})
    API->>SubEngine: Validate & activate Subscription
    SubEngine-->>API: ✓ Subscription active
    API-->>SubMgr: Subscription ID

    SubMgr-->>API: Registration complete<br/>(Status: Active)
    API-->>User: 200 OK

    Note over User,RefreshChan: Phase 2: Initial Population (Async)

    Queue->>PopJob: Dequeue OrchestratorJob
    PopJob->>Queue: Enqueue ProcessingJob(Patient, batch=100)

    loop For each batch of resources
        Queue->>PopJob: Dequeue ProcessingJob
        PopJob->>SearchSvc: SearchAsync(Patient, _count=100, ct=...)
        SearchSvc-->>PopJob: [Patient/p1, Patient/p2, ...]

        loop For each resource
            PopJob->>Evaluator: Evaluate(viewDef, patient)
            Evaluator-->>PopJob: [{id: "p1", gender: "female", ...}]
            PopJob->>Materializer: UpsertResourceAsync(rows, "Patient/p1")
            Materializer->>SQL: DELETE + INSERT sqlfhir.patient_demographics
        end

        alt More resources exist
            PopJob->>Queue: Enqueue next ProcessingJob(ct=nextToken)
        end
    end

    Note over User,RefreshChan: Phase 3: Incremental Updates (Subscription-Driven)

    User->>API: POST Patient<br/>(new patient created)
    API->>SQL: dbo.Resource INSERT (Patient/new-1)

    SQL->>SubEngine: Transaction committed
    SubEngine->>SubEngine: Match criteria: "Patient?"<br/>→ subscription matches
    SubEngine->>Queue: Enqueue SubscriptionProcessingJob<br/>(resources: [Patient/new-1])

    Queue->>SubEngine: Dequeue ProcessingJob
    SubEngine->>RefreshChan: PublishAsync([Patient/new-1])

    RefreshChan->>Evaluator: Evaluate(viewDef, Patient/new-1)
    Evaluator-->>RefreshChan: [{id: "new-1", gender: "male", ...}]
    RefreshChan->>Materializer: UpsertResourceAsync(rows, "Patient/new-1")
    Materializer->>SQL: DELETE + INSERT sqlfhir.patient_demographics

    Note over SQL: Row appears in<br/>sqlfhir.patient_demographics<br/>within seconds

    Note over User,RefreshChan: Phase 4: Query Results

    User->>API: GET ViewDefinition/patient_demographics/$run<br/>?_format=csv
    API->>SQL: SELECT * FROM sqlfhir.patient_demographics
    SQL-->>API: [all rows including new-1]
    API-->>User: 200 OK (CSV)<br/>id,gender,birth_date<br/>new-1,male,1995-07-20<br/>...
```

## Consequences

### Positive
- **Sub-second data freshness**: Materialized views update as FHIR resources change, eliminating batch ETL
- **Standard-based**: Uses two complementary FHIR specs (SQL on FHIR v2 + Subscriptions)
- **Pluggable targets**: SQL Server for operational analytics, Parquet for Fabric/Spark/research
- **Leverages existing infrastructure**: Reuses subscription engine, job framework, SQL retry service
- **Ignixa integration**: Avoids building a custom FHIRPath engine and ViewDefinition runner from scratch

### Negative
- **Initial population cost**: Full table scan of all resources of a type (future optimization: translate FHIRPath where clauses to search queries)
- **Over-triggering**: Broad subscription criteria (e.g., `Observation?`) fires for all observations, not just those matching the ViewDefinition's where clause
- **In-memory registration state**: ViewDefinition→Subscription mapping is in-memory; requires re-registration on server restart
- **SQL injection surface**: Dynamic DDL generation requires careful identifier validation (implemented via regex)

### Risks
- **Ignixa package stability**: External dependency (MIT licensed, net9.0 only)
- **Scale under high write volume**: Each resource change triggers ViewDefinition re-evaluation; batching mitigates but doesn't eliminate
- **Schema evolution**: ViewDefinition column changes require table recreation (not in-place ALTER)

## Components Built

| Component | Location | Purpose |
|-----------|----------|---------|
| ViewDefinitionEvaluator | SqlOnFhir/ | Bridges Firely SDK ↔ Ignixa IElement |
| SqlServerViewDefinitionSchemaManager | SqlOnFhir/Materialization/ | CREATE TABLE DDL in sqlfhir schema |
| SqlServerViewDefinitionMaterializer | SqlOnFhir/Materialization/ | Atomic DELETE+INSERT row upserts |
| ParquetViewDefinitionMaterializer | SqlOnFhir/Materialization/ | Parquet files to Azure Blob/ADLS |
| MaterializerFactory | SqlOnFhir/Materialization/ | Routes to SQL, Parquet, or both |
| FhirTypeToSqlTypeMap | SqlOnFhir/Materialization/ | FHIR→SQL Server type mapping |
| ViewDefinitionRefreshChannel | SqlOnFhir/Channels/ | ISubscriptionChannel for incremental updates |
| ViewDefinitionSubscriptionManager | SqlOnFhir/Channels/ | Registration lifecycle + auto-subscription |
| PopulationOrchestratorJob | SqlOnFhir/Materialization/Jobs/ | Creates table, enqueues processing |
| PopulationProcessingJob | SqlOnFhir/Materialization/Jobs/ | Batch search → evaluate → materialize |
| ViewDefinitionRunHandler | SqlOnFhir/Operations/ | $viewdefinition-run (sync eval or table read) |
| ViewDefinitionExportHandler | SqlOnFhir/Operations/ | $viewdefinition-export (fast-path or async) |
| ViewDefinitionRunController | Shared.Api/Controllers/ | HTTP endpoints for $run and $export |

## References
- [SQL on FHIR v2 Spec](https://build.fhir.org/ig/FHIR/sql-on-fhir-v2/)
- [SQL on FHIR Operations](https://build.fhir.org/ig/FHIR/sql-on-fhir-v2/operations.html)
- [FHIR Subscriptions Backport IG](http://hl7.org/fhir/uv/subscriptions-backport/)
- [Ignixa FHIR](https://github.com/brendankowitz/ignixa-fhir)

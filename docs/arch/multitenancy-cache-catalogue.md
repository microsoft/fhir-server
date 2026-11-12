# FHIR Server Cache Catalogue for Multi-Tenancy

Feature 206292 · US 210971 (catalogue) · US 211098 (cache management) · ADR `adr-2610-multitenancy` · Design: [adr-2611-partition-cache-management.md](adr-2611-partition-cache-management.md)

## Context

- **FHIR service** = the product a customer provisions. One app (one or more instances) serves all of its partitions.
- **Partition** = one independent FHIR server inside a FHIR service, with its own database. Addressed as `<base-url>/partition/<partition-name>/...`.
- Each type of cached data gets its own cache, which holds the data for every partition (`PartitionCache<T>`), or one value per app for shared caches (`Cache<T>`). (Earlier drafts and PR #6017 call partitions "tenants".)
- The generic cache classes live in **Microsoft.Health.Fhir.Core**; SQL-loaded caches build on `SqlPartitionCache<T>` in **Microsoft.Health.Fhir.SqlServer**.
- All code that reads cached data will go through these caches instead of keeping its own copy. Callers move over in phases, one cache type at a time.
- The cache management design is described in [adr-2611-partition-cache-management.md](adr-2611-partition-cache-management.md). This document is the inventory of caches it covers.

**Decisions**
- Only FHIR spec search parameters (`search-parameters.json`) are shared, at global level. Microsoft default (`ms-search-parameters.json`) and custom search parameters are per partition, because they can change and need reindex per database.
- Each cache keeps its current refresh behavior and timer, run per partition.
- `dbo.Parameters` values stay per partition.
- The customer-managed key is per FHIR service.
- Every partition's caches stay loaded (no lazy loading or eviction).

**Levels used below**
- **Global** – built into the server, so identical for every FHIR service running the same build. One copy per app.
- **FHIR service** – comes from the FHIR service's configuration. One copy per FHIR service (one per app today), shared by all of its partitions.
- **Partition** – comes from one partition's database. One copy per partition.

Global and FHIR service caches use `Cache<T>`; partition caches use `PartitionCache<T>`.

## Summary

| # | Cache | What it does | Loaded | Refreshed | Level | Refactor effort | Phase |
|---|---|---|---|---|---|---|---|
| 1 | Spec search parameters | Search parameters defined by the FHIR specification | App startup | Never | Global | Medium | 2 |
| 2 | Built-in reference data | Compartments, code system map, compiled FHIRPath, FHIR spec package | Startup or first use | Never (spec package re-read every 4 h) | Global | Medium | 2 |
| 3 | App configuration caches | RBAC roles, identity provider endpoints and keys, container registry tokens | Startup or first use | Roles never; endpoints, keys and tokens on expiry | FHIR service | Low | 1 |
| 4 | Customer-managed key health | Whether the customer-managed encryption key is healthy | First health check | Set by the shared package | FHIR service | Low | 1 |
| 5 | Custom and Microsoft default search parameters, and statuses | Search parameters that can change per database, and the status of every parameter | Startup | Every 20 s, and before any SearchParameter write | Partition | High | 2 |
| 6 | SQL ID maps | Maps resource types, search parameters, claim and compartment types to SQL IDs | Startup | On schema upgrade; new search parameter IDs added by #5 | Partition | High | 2 |
| 7 | Code system and unit IDs | Maps token systems and quantity units to SQL IDs | Startup, then on first use of a value | Entries expire after 24 h | Partition | Medium | 2 |
| 8 | Schema version | Decides which SQL features and procedures can be used | Startup | Polled by the shared schema package | Partition | Medium | 2 |
| 9 | Capability statement | Built capability statement used for "is X supported?" checks | First request that needs it | Never | Partition | High | 3 |
| 10 | Metadata document | The finished `/metadata` response returned to clients | First `/metadata` request | Cleared every 4 h or when search parameters or profiles change; rebuilt on next request | Partition | Medium | 3 |
| 11 | Profiles | Customer StructureDefinition, ValueSet and CodeSystem resources for validation | First use | Every 4 h; other instances detect changes within ~5 min | Partition | Medium | 3 |
| 12 | Profile validator | Reused Firely validator wired to the spec package and the partition's profiles | First validation | Every 30 min | Partition | Medium | 3 |
| 13 | Custom query procedures | Hand-tuned stored procedures used for specific searches | First search | Expires after 60 s | Partition | Low | 1 |
| 14 | SQL statistics list | Which SQL statistics already exist, so they aren't created again | First search | Never | Partition | Low | 1 |
| 15 | `dbo.Parameters` values | Feature flags and settings ops change in the database | First use | Expires after 10 min | Partition | Low | 1 |
| 16 | Skewed search parameters | Parameters with uneven data, where query plans shouldn't be reused (off by default) | After search parameters load | Every 1 h | Partition | Low | 1 |
| 17 | Change feed resource type names | Converts resource type IDs to names in the change feed | First change feed read | Never | Partition | Low | 2 |
| 18 | Search helper lookups | Query generator, composite value type and reference targets per search parameter | First use | Never | Partition | Low | 2 |

Refactor effort = how hard it is to move existing callers to read from the new caches.

## Rollout phases

Each phase is one PR. Phases are ordered by dependency, so each PR can merge on its own.

| Phase | Caches | Why together |
|---|---|---|
| 1 | Cache classes + #3 App configuration caches, #4 Customer-managed key health, #13 Custom query procedures, #14 SQL statistics list, #15 `dbo.Parameters` values, #16 Skewed search parameters | Adds `Cache<T>`, `PartitionCache<T>`, `ScopedCache<T>`, `SqlPartitionCache<T>` and the `CacheInitializer`, and moves the small, mostly independent caches. Low risk; sets the pattern. |
| 2 | #1 Spec search parameters, #2 Built-in reference data (except the FHIR spec package), #5 Custom and Microsoft default search parameters, #6 SQL ID maps, #7 Code system and unit IDs, #8 Schema version, #17 Change feed resource type names, #18 Search helper lookups | Tightly linked: they load together at startup, the search parameter refresh feeds the SQL ID maps, and the helper lookups are built from search parameters. Splits shared spec parameters from per-partition ones. Largest PR; can be split further if needed. #8 depends on the shared-package schema work. |
| 3 | #9 Capability statement, #10 Metadata document, #11 Profiles, #12 Profile validator, FHIR spec package from #2 | Built on top of phase 2: the capability statement and metadata document use search parameters and profiles, and the validator uses profiles and the spec package. |

---

## Global caches

### 1. Spec search parameters
- **What it does:** Definitions of the search parameters from the FHIR specification.
- **Loads from:** Embedded `search-parameters.json`.
- **When:** App startup.
- **Refresh:** Never. Spec parameters are treated as always enabled for every partition, so they need no per-partition status or reindex. Today each database also stores a status for every spec parameter (new ones start as `Supported` until reindex); that per-database status stops being used.
- **Used by:** Search, indexing, sorting, capability statement.
- **Code:** `SearchParameterDefinitionManager` (Core).
- **Refactor effort:** Medium – today all search parameters live in the same dictionaries, and statuses are stored on the shared objects. Spec parameters need to be split out.

### 2. Built-in reference data
- **What it does:** Compartment definitions, resource path to code system map, compiled FHIRPath expressions, FHIR spec package used by the validator.
- **Loads from:** Embedded files and compiled code.
- **When:** Startup or first use.
- **Refresh:** Never. The validator's copy of the spec package is re-read every 4 hours.
- **Code:** `CompartmentDefinitionManager`, `CodeSystemResolver`, `TypedElementSearchIndexer`, `ProfileValidator` (spec package).
- **Refactor effort:** Medium – `TypedElementSearchIndexer` and `ProfileValidator` also hold per-partition caches (#18, #12), so the shared parts need to be split out of those classes.

## FHIR service caches

### 3. App configuration caches
- **What it does:** RBAC roles, identity provider endpoints and signing keys, container registry tokens and templates.
- **Loads from:** App settings (`roles.json`), identity provider, Azure Container Registry.
- **When:** Roles at startup; endpoints, keys and tokens on first use.
- **Refresh:** Roles never; endpoints after 24 hours; keys and tokens on expiry.
- **Code:** `RoleLoader`, `RoleBasedFhirAuthorizationService`, `OidcDiscoveryService`, `DefaultTokenIntrospectionService`, `ContainerRegistryTemplateProvider`, `DefaultTemplateProvider`, `AnonymizationConfigurationArtifactProvider`.
- **Refactor effort:** Low – mostly singletons. Note: `AnonymizationConfigurationArtifactProvider` is transient today, so its token cache is not reused between calls.

### 4. Customer-managed key health
- **What it does:** Whether the customer-managed encryption key is healthy, so health checks do not hit the database every time.
- **Loads from:** Database / Key Vault check in the shared package.
- **When / Refresh:** Set by the shared package.
- **Code:** `ValueCache<CustomerKeyHealth>` (shared package; registered in SqlServer, read by `SqlStatusReporter`).
- **Refactor effort:** Low – one key per FHIR service, held as a singleton today.

---

## Partition caches

### 5. Custom and Microsoft default search parameters, and statuses
- **What it does:** Customer-defined and Microsoft default search parameters, the status of every parameter (enabled, supported, disabled, pending delete), and a per-resource-type hash of the search parameters that is stored on each written resource and used by reindex.
- **Loads from:** Embedded `ms-search-parameters.json`; SearchParameter resources in `dbo.Resource`; statuses in `dbo.SearchParam`.
- **When:** Startup, after the SQL ID maps are loaded.
- **Refresh:** Every 20 seconds (`SearchParameterCacheRefreshIntervalSeconds`), reading only rows changed since the last refresh. Also refreshed before any SearchParameter write. Each instance logs its progress to `dbo.EventLog`; reindex uses this to check that all instances are in sync.
- **Used by:** Search, indexing, sorting, reindex, capability statement.
- **Code:** `SearchParameterDefinitionManager`, `SearchParameterStatusManager`, `SearchParameterOperations`, `SearchParameterCacheRefreshBackgroundService` (Core).
- **Refactor effort:** High – `ISearchParameterDefinitionManager` is used in about 30 files. Microsoft default parameters are per partition because they can change between releases and need reindex per database.

### 6. SQL ID maps
- **What it does:** Maps names to the small integer IDs stored in SQL tables: resource types, search parameters, claim types, compartment types.
- **Loads from:** `dbo.ResourceType`, `dbo.SearchParam`, `dbo.ClaimType`, `dbo.CompartmentType` (via `dbo.InitializeBase`).
- **When:** Startup and after a schema upgrade. New search parameter IDs are added by the refresh in #5.
- **Refresh:** No timer. IDs never change once assigned.
- **Used by:** Every SQL write (index rows) and every search query.
- **Code:** `SqlServerFhirModel` (SqlServer).
- **Refactor effort:** High – `SqlServerFhirModel` / `ISqlServerFhirModel` are used in about 30 files, including all row generators. Highest risk: one partition's IDs used against another partition's database writes wrong index rows.

### 7. Code system and unit IDs
- **What it does:** Maps token code systems and quantity units to integer IDs.
- **Loads from:** `dbo.System`, `dbo.QuantityCode`. On a miss, `dbo.GetSystemId` / `dbo.GetQuantityCodeId`.
- **When:** Preloaded at startup, then on first use of each value.
- **Refresh:** Entries expire after 24 hours. Limit of 50 MB per cache.
- **Used by:** Token and quantity index writes and searches.
- **Code:** `SqlServerFhirModel` (`_systemToId`, `_quantityCodeToId`).
- **Refactor effort:** Medium – already a separate class (`FhirMemoryCache`).

### 8. Schema version
- **What it does:** The database schema version, which decides which SQL features and stored procedures can be used.
- **Loads from:** Schema version tables (shared `Microsoft.Health.SqlServer` package).
- **When:** Startup.
- **Refresh:** Polled by the shared schema package.
- **Code:** `SchemaInformation` (shared package).
- **Refactor effort:** Medium – the type lives in the shared package. A per-database schema interface is being designed separately.

### 9. Capability statement
- **What it does:** The built capability statement used to answer checks such as "does this server support update on Patient?", plus the cached answers to those checks.
- **Loads from:** Built from #1, #5, #11 and app configuration. Includes the partition's base URL.
- **When:** First request that needs it (`/metadata`, format checks, "is X supported?" checks).
- **Refresh:** Never. It is built once; the cached answers are also never cleared.
- **Code:** `SystemConformanceProvider` (`_listedCapabilityStatement`), `CapabilityStatementBuilder`, `ConformanceProviderBase` (cached answers) (Core).
- **Refactor effort:** High – singleton with its own background loop, shared with #10.

### 10. Metadata document
- **What it does:** The finished `/metadata` response returned to clients.
- **Loads from:** Built from the capability statement builder (#9) with the latest search parameters and profiles.
- **When:** First `/metadata` request.
- **Refresh:** Cleared every 4 hours (`SystemConformanceProviderRebuildIntervalSeconds`), when #5 picks up a search parameter change, or when #11 detects a profile change (checked every 60 seconds). Rebuilt on the next `/metadata` request.
- **Code:** `SystemConformanceProvider` (`_metadata`) (Core).
- **Refactor effort:** Medium – moves together with #9.

### 11. Profiles
- **What it does:** The customer's StructureDefinition, ValueSet and CodeSystem resources.
- **Loads from:** `dbo.Resource` (search by resource type).
- **When:** First use.
- **Refresh:** Every 4 hours (`ValidateOperationConfiguration.CacheDurationInSeconds`). After a profile write on the same instance, reloaded on next use. Other instances detect changes within about 5 minutes (`BackgroundProfileStatusCheckIntervalInSeconds`).
- **Used by:** `$validate`, profile validation on create/update, supported profiles in the capability statement.
- **Code:** `ServerProvideProfileValidation` (Shared.Core).
- **Refactor effort:** Medium – used in about 13 files, but the class is self-contained.

### 12. Profile validator
- **What it does:** A reused Firely validator wired to the spec package and the partition's profiles, so it is not rebuilt per request.
- **Loads from:** FHIR spec package (#2) + the partition's profiles (#11).
- **When:** First validation.
- **Refresh:** Rebuilt every 30 minutes (hard-coded).
- **Code:** `ProfileValidator` (Shared.Core).
- **Refactor effort:** Medium – moves together with #11.

### 13. Custom query procedures
- **What it does:** Lets a search use a hand-tuned stored procedure (`CustomQuery_<hash>`) when one exists for its query hash.
- **Loads from:** `sys.objects`.
- **When:** First search.
- **Refresh:** Expires after 60 seconds; reloaded on the next search.
- **Code:** `CustomQueries` (static, SqlServer).
- **Refactor effort:** Low – small static class.

### 14. SQL statistics list
- **What it does:** Remembers which SQL statistics already exist on search tables so they are not created again.
- **Loads from:** `sys.stats` (via `dbo.GetResourceSearchParamStats`).
- **When:** First search.
- **Refresh:** Never.
- **Code:** `SqlServerSearchService._resourceSearchParamStats` (static).
- **Refactor effort:** Low.

### 15. `dbo.Parameters` values
- **What it does:** Settings ops can change in the database without redeploying: feature flags (for example `MergeResources.RawResourceDeduping.IsEnabled`), long-running query thresholds, read replica traffic ratio, event log connection string.
- **Loads from:** `dbo.Parameters`.
- **When:** First use.
- **Refresh:** Expires after 10 minutes. The event log connection string is never refreshed.
- **Code:** `CachedParameter` (static fields in `SqlServerSearchService`, `SqlServerFhirDataStore`), `SqlRetryService` (static), `SqlServerParameterStore` (no callers in this repo).
- **Refactor effort:** Low – mostly statics that need one copy per partition.

### 16. Skewed search parameters
- **What it does:** Search parameters with very uneven data, for which SQL should not reuse a cached query plan.
- **Loads from:** SQL statistics (via `dbo.GetResourceSearchParamStatsProperties`) and `dbo.SearchParam`.
- **When:** After search parameters load. Off by default.
- **Refresh:** Every hour.
- **Code:** `QueryPlanReuseChecker` (SqlServer).
- **Refactor effort:** Low.

### 17. Change feed resource type names
- **What it does:** Converts resource type IDs back to names when reading the change feed.
- **Loads from:** `dbo.ResourceType`.
- **When:** First change feed read.
- **Refresh:** Never.
- **Code:** `SqlServerFhirResourceChangeDataStore` (static).
- **Refactor effort:** Low – duplicates #6; replace with #6.

### 18. Search helper lookups
- **What it does:** Small lookups built from search parameter definitions: query generator per parameter, value type of composite parameters, target types of reference parameters.
- **Loads from:** Computed from #1 and #5.
- **When:** First use.
- **Refresh:** Never.
- **Code:** `SearchParamTableExpressionQueryGeneratorFactory`, `SearchParameterToSearchValueTypeMap`, `TypedElementSearchIndexer`.
- **Refactor effort:** Low – keyed by URL, so the same custom URL defined differently in two partitions would collide.

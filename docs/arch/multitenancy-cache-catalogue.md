# FHIR Server Cache Catalogue for Multi-Tenancy

Feature 206292 · US 210971 (catalogue) · US 211098 (cache management) · ADR `adr-2610-multitenancy`

## Context

- **Tenant** = one FHIR service = one database.
- **Tenant group** = one customer = one app (one or more instances) serving all of the group's tenants.
- Each tenant gets its own **cache manager**. The **tenant manager** holds one per tenant.
- Cache managers live in **Microsoft.Health.Fhir.Core**. They are not tied to SQL; SQL is only where the data comes from.
- All code that reads cached data will go through the cache manager instead of keeping its own copy. Callers move over in phases, one cache type at a time.
- For now, every tenant's caches stay loaded (no lazy loading or eviction).

**Levels used below**
- **Tenant group** – one shared copy per app. Only for data that is identical for every tenant.
- **Tenant** – one copy per tenant database.

## Summary

| # | Cache | What it does | Loaded | Refreshed | Level | Refactor effort | Phase |
|---|---|---|---|---|---|---|---|
| 1 | Spec search parameters | Search parameters defined by the FHIR specification | App startup | Never | Tenant group | Medium | 2 |
| 2 | Built-in reference data | Compartments, code system map, compiled FHIRPath, FHIR spec package | Startup or first use | Never | Tenant group | None | — |
| 3 | App configuration caches | RBAC roles, identity provider keys, container registry tokens | Startup or first use | Roles never; keys and tokens on expiry | Tenant group | None | — |
| 4 | Custom search parameters and statuses | Customer-defined search parameters and the status of every parameter | Startup | Every 20 s, and before any SearchParameter write | Tenant | High | 2 |
| 5 | SQL ID maps | Maps resource types, search parameters, claim and compartment types to SQL IDs | Startup | On schema upgrade; new search parameter IDs added by #4 | Tenant | High | 2 |
| 6 | Code system and unit IDs | Maps token systems and quantity units to SQL IDs | Startup, then on first use of a value | Entries expire after 24 h | Tenant | Medium | 2 |
| 7 | Schema version | Decides which SQL features and procedures can be used | Startup | Polled by the shared schema package | Tenant | Medium | 2 |
| 8 | Capability statement | The `/metadata` document and "is X supported?" checks | First `/metadata` request | Checked every 60 s; rebuilt every 4 h or when search parameters or profiles change | Tenant | High | 3 |
| 9 | Profiles | Customer StructureDefinition, ValueSet and CodeSystem resources for validation | First use | Every 4 h, or after a profile write on the same instance | Tenant | Medium | 3 |
| 10 | Profile validator | Compiled Firely validator built from the spec and the tenant's profiles | First validation | Every 30 min | Tenant | Medium | 3 |
| 11 | Custom query procedures | Hand-tuned stored procedures used for specific searches | First search | Every 60 s | Tenant | Low | 1 |
| 12 | SQL statistics list | Which SQL statistics already exist, so they aren't created again | First search needing stats | Never | Tenant | Low | 1 |
| 13 | `dbo.Parameters` values | Feature flags and settings ops change in the database | First use | Every 10 min | Tenant | Low | 1 |
| 14 | Skewed search parameters | Parameters with uneven data, where query plans shouldn't be reused (off by default) | After search parameters load | Every 1 h | Tenant | Low | 1 |
| 15 | Change feed resource type names | Converts resource type IDs to names in the change feed | First change feed read | Never | Tenant | Low | 2 |
| 16 | Customer-managed key health | Whether the customer-managed encryption key is healthy | First health check | Set by the shared package | Tenant | Low | 1 |
| 17 | Search helper lookups | Query generator, composite value type and reference targets per search parameter | First use | Never | Tenant | Low | 2 |

Refactor effort = how hard it is to move existing callers to read from the cache manager.

## Rollout phases

Each phase is one PR. Phases are ordered by dependency, so each PR can merge on its own.

| Phase | Caches | Why together |
|---|---|---|
| 1 | Cache manager + #11 Custom query procedures, #12 SQL statistics list, #13 `dbo.Parameters` values, #14 Skewed search parameters, #16 Customer-managed key health | Adds the per-tenant cache manager and moves the small, self-contained SQL caches (mostly statics). Low risk; sets the pattern. |
| 2 | #1 Spec search parameters, #4 Custom search parameters and statuses, #5 SQL ID maps, #6 Code system and unit IDs, #7 Schema version, #15 Change feed resource type names, #17 Search helper lookups | Tightly linked: they load together at startup, the search parameter refresh feeds the SQL ID maps, and the helper lookups are built from search parameters. Splits spec (shared) from custom (per tenant). Largest PR; can be split further if needed. #7 depends on the shared-package schema work. |
| 3 | #8 Capability statement, #9 Profiles, #10 Profile validator | Built on top of phase 2: the capability statement uses search parameters and profiles, and the validator uses profiles. |
| — | #2 Built-in reference data, #3 App configuration caches | Already shared at tenant group level. No change. |

---

## Tenant group caches

### 1. Spec search parameters
- **What it does:** Definitions of the search parameters from the FHIR specification.
- **Loads from:** Embedded `search-parameters.json`.
- **When:** App startup.
- **Refresh:** Never. Spec parameters do not change and are always enabled, so no per-tenant status or reindex tracking.
- **Used by:** Search, indexing, sorting, capability statement.
- **Code:** `SearchParameterDefinitionManager` (Core).
- **Refactor effort:** Medium – today spec and custom parameters live in the same dictionaries, and statuses are stored on the shared objects. They need to be split.

### 2. Built-in reference data
- **What it does:** Compartment definitions, resource path to code system map, compiled FHIRPath expressions, FHIR spec package used by the validator.
- **Loads from:** Embedded files and compiled code.
- **When:** Startup or first use.
- **Refresh:** Never.
- **Code:** `CompartmentDefinitionManager`, `CodeSystemResolver`, `TypedElementSearchIndexer`, `ProfileValidator` (spec package).
- **Refactor effort:** None – already safe to share.

### 3. App configuration caches
- **What it does:** RBAC roles, identity provider signing keys, container registry tokens.
- **Loads from:** App settings (`roles.json`), identity provider, Azure Container Registry.
- **Refresh:** Roles at startup; keys and tokens on expiry.
- **Refactor effort:** None – app configuration is shared by the group. Revisit only if tenants need different roles.

---

## Tenant caches

### 4. Custom search parameters and statuses
- **What it does:** Customer-defined search parameters, plus the status of every parameter (enabled, supported, disabled, pending delete).
- **Loads from:** SearchParameter resources in `dbo.Resource`; statuses in `dbo.SearchParam`.
- **When:** Startup, after the SQL ID maps are loaded.
- **Refresh:** Every 20 seconds, reading only rows changed since the last refresh. Also refreshed before any SearchParameter write. Each instance logs its progress to `dbo.EventLog`; reindex uses this to check that all instances are in sync.
- **Used by:** Search, indexing, sorting, reindex, capability statement.
- **Code:** `SearchParameterDefinitionManager`, `SearchParameterStatusManager`, `SearchParameterOperations`, `SearchParameterCacheRefreshBackgroundService` (Core).
- **Refactor effort:** High – `ISearchParameterDefinitionManager` is used in about 28 files.

### 5. SQL ID maps
- **What it does:** Maps names to the small integer IDs stored in SQL tables: resource types, search parameters, claim types, compartment types.
- **Loads from:** `dbo.ResourceType`, `dbo.SearchParam`, `dbo.ClaimType`, `dbo.CompartmentType` (via `dbo.InitializeBase`).
- **When:** Startup and after a schema upgrade. New search parameter IDs are added by the refresh in #4.
- **Refresh:** No timer. IDs never change once assigned.
- **Used by:** Every SQL write (index rows) and every search query.
- **Code:** `SqlServerFhirModel` (SqlServer).
- **Refactor effort:** High – `ISqlServerFhirModel` is used in about 10 files, including all row generators. Highest risk: one tenant's IDs used against another tenant's database writes wrong index rows.

### 6. Code system and unit IDs
- **What it does:** Maps token code systems and quantity units to integer IDs.
- **Loads from:** `dbo.System`, `dbo.QuantityCode`. On a miss, `dbo.GetSystemId` / `dbo.GetQuantityCodeId`.
- **When:** Preloaded at startup, then on first use of each value.
- **Refresh:** Entries expire after 24 hours. Limit of 50 MB per cache.
- **Used by:** Token and quantity index writes and searches.
- **Code:** `SqlServerFhirModel` (`_systemToId`, `_quantityCodeToId`).
- **Refactor effort:** Medium – already a separate class (`FhirMemoryCache`). Consider one memory budget for the whole group instead of 50 MB per tenant.

### 7. Schema version
- **What it does:** The database schema version, which decides which SQL features and stored procedures can be used.
- **Loads from:** Schema version tables (shared `Microsoft.Health.SqlServer` package).
- **When:** Startup.
- **Refresh:** Polled by the shared schema package.
- **Code:** `SchemaInformation` (shared package).
- **Refactor effort:** Medium – the type lives in the shared package. A per-database schema interface is being designed separately.

### 8. Capability statement
- **What it does:** The `/metadata` document, and answers to checks such as "does this server support update on Patient?".
- **Loads from:** Built from #1, #4, #9 and app configuration. Includes the tenant's base URL.
- **When:** First `/metadata` request.
- **Refresh:** Checked every 60 seconds; fully rebuilt every 4 hours; rebuilt when search parameters or profiles change on the same instance.
- **Code:** `SystemConformanceProvider` (Core).
- **Refactor effort:** High – singleton with its own background loop.

### 9. Profiles
- **What it does:** The customer's StructureDefinition, ValueSet and CodeSystem resources.
- **Loads from:** `dbo.Resource` (search by resource type).
- **When:** First use.
- **Refresh:** Every 4 hours (`ValidateOperationConfiguration.CacheDurationInSeconds`), or immediately after a profile write on the same instance. Other instances can be up to 4 hours behind.
- **Used by:** `$validate`, profile validation on create/update, supported profiles in the capability statement.
- **Code:** `ServerProvideProfileValidation` (Shared.Core).
- **Refactor effort:** Medium – used in about 15 files, but the class is self-contained.

### 10. Profile validator
- **What it does:** A compiled Firely validator, so it is not rebuilt per request.
- **Loads from:** FHIR spec package (#2) + the tenant's profiles (#9).
- **When:** First validation.
- **Refresh:** Rebuilt every 30 minutes.
- **Code:** `ProfileValidator` (Shared.Core).
- **Refactor effort:** Medium – moves together with #9.

### 11. Custom query procedures
- **What it does:** Lets a search use a hand-tuned stored procedure (`CustomQuery_<hash>`) when one exists for its query hash.
- **Loads from:** `sys.objects`.
- **When:** First search.
- **Refresh:** Every 60 seconds.
- **Code:** `CustomQueries` (static, SqlServer).
- **Refactor effort:** Low – small static class. Note: query hashes now include the tenant database name.

### 12. SQL statistics list
- **What it does:** Remembers which SQL statistics already exist on search tables so they are not created again.
- **Loads from:** `sys.stats` (via `dbo.GetResourceSearchParamStats`).
- **When:** First search that needs statistics.
- **Refresh:** Never.
- **Code:** `SqlServerSearchService._resourceSearchParamStats` (static).
- **Refactor effort:** Low.

### 13. `dbo.Parameters` values
- **What it does:** Settings ops can change in the database without redeploying: feature flags (for example `MergeResources.RawResourceDeduping.IsEnabled`), long-running query thresholds, read replica traffic ratio, event log connection string.
- **Loads from:** `dbo.Parameters`.
- **When:** First use.
- **Refresh:** Every 10 minutes. The event log connection string is never refreshed.
- **Code:** `CachedParameter` (static fields in `SqlServerSearchService`, `SqlServerFhirDataStore`), `SqlRetryService` (static), `SqlServerParameterStore`.
- **Refactor effort:** Low – mostly statics that need one copy per tenant.

### 14. Skewed search parameters
- **What it does:** Search parameters with very uneven data, for which SQL should not reuse a cached query plan.
- **Loads from:** SQL statistics (via `dbo.GetResourceSearchParamStatsProperties`).
- **When:** After search parameters load. Off by default.
- **Refresh:** Every hour.
- **Code:** `QueryPlanReuseChecker` (SqlServer).
- **Refactor effort:** Low.

### 15. Change feed resource type names
- **What it does:** Converts resource type IDs back to names when reading the change feed.
- **Loads from:** `dbo.ResourceType`.
- **When:** First change feed read.
- **Refresh:** Never.
- **Code:** `SqlServerFhirResourceChangeDataStore` (static).
- **Refactor effort:** Low – duplicates #5; replace with #5.

### 16. Customer-managed key health
- **What it does:** Whether the customer-managed encryption key is healthy, so health checks do not hit the database every time.
- **Loads from:** Database / Key Vault check in the shared package.
- **Code:** `ValueCache<CustomerKeyHealth>` (SqlServer).
- **Refactor effort:** Low. Could be tenant group level if the key is set on the server or pool rather than per database.

### 17. Search helper lookups
- **What it does:** Small lookups built from search parameter definitions: query generator per parameter, value type of composite parameters, target types of reference parameters.
- **Loads from:** Computed from #1 and #4.
- **When:** First use.
- **Refresh:** Never.
- **Code:** `SearchParamTableExpressionQueryGeneratorFactory`, `SearchParameterToSearchValueTypeMap`, `TypedElementSearchIndexer`.
- **Refactor effort:** Low – keyed by URL, so the same custom URL defined differently in two tenants would collide.

---

## Open questions

1. **Microsoft default search parameters** (`ms-search-parameters.json`): shared like spec parameters, or per tenant? New ones may need a reindex on existing databases.
2. **Refresh:** one refresh loop per tenant group that goes over all tenants, or one per cache manager?
3. **`dbo.Parameters` values:** keep them per database, or move them to app configuration?
4. **Customer-managed key:** set per database or per server/pool?
5. **Memory:** measure one tenant's cache manager before deciding how many tenants fit in a group.

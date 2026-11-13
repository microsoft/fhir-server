# ADR 2611: Partition cache management

## Context
- With multi-tenancy (`adr-2610-multitenancy`), one app serves a FHIR service that contains several partitions. Each partition is an independent FHIR server with its own database, addressed as `<base-url>/partition/<partition-name>`.
- The server keeps about 18 caches, listed in [multitenancy-cache-catalogue.md](multitenancy-cache-catalogue.md). Today each one is a singleton or static loaded from the single database, and each class has its own load and refresh logic. This makes it hard to know what is cached, where it is used and how it is refreshed.

## Decision
We will centralize caching in a small set of generic cache classes. Each type of cached data gets its own cache, and each cache holds the data for every partition. Callers ask for the type they need and get the value for the current partition.

1. **Cache classes.**
   - `Cache<T>` (singleton): one value per app. Used for global and FHIR service caches (e.g. spec search parameters, roles).
   - `PartitionCache<T>` (singleton): one value per partition, keyed by partition ID. Used for partition caches (e.g. custom search parameters, SQL ID maps).
   - `ScopedCache<T>` (scoped): a view of `PartitionCache<T>` for the current request's partition. Most callers inject this.
   - Each cache type subclasses `Cache<T>` or `PartitionCache<T>` and implements only how to load its data. The concrete classes are the DI service types; no separate interfaces.
2. **Placement.** The generic classes live in `Microsoft.Health.Fhir.Core`. An abstract `SqlPartitionCache<T>` in `Microsoft.Health.Fhir.SqlServer` holds the common "connect to this partition's database" code for SQL-loaded caches (SQL ID maps, custom query procedures, SQL statistics, `dbo.Parameters` values).
3. **Sync and async access.** Caches expose `Get()` and `GetAsync()`, sharing one loader, `GetRemoteDataAsync(partitionId, bool async, CancellationToken)`, as in the Azure SDK: `async: false` uses synchronous SQL calls, so `Get()` never blocks on a task (`.Result`, `.GetAwaiter().GetResult()`). Existing synchronous callers keep using `Get()`, so we don't make code async where it has no need to be. A cache may offer only `GetAsync()`.
   - Where an operation already runs async (search, merge), it can `await` the value once and pass it into synchronous code, such as the SQL query generator context and the TVP row generators.
   - System and quantity code IDs are get-or-insert on a miss. They are resolved with `GetAsync()` before TVP rows are generated, so row generation only reads memory.
4. **Preload.** A `CacheInitializer` hosted service loads every cache for every partition at startup. For each partition it creates a DI scope, sets that partition's ID on the scope's request context, and calls `GetAsync()` on each `ScopedCache<T>`. The caches are listed explicitly, so no common cache interface is needed; a cache missing from the list loads on its first `Get()`. Global and FHIR service caches are loaded once with `Cache<T>.GetAsync()`. No eviction for now; an LRU can be added later as an incremental change, since we don't want to keep many partitions loaded permanently.
5. **Dependent caches.** Caches stay separate. A cache that is built from another cache (e.g. SQL ID maps and search helper lookups from search parameters, `/metadata` from search parameters and profiles) takes it as a dependency and listens for its change notifications, similar to `IOptionsMonitor<T>`. Notifications include the partition ID.
6. **Refresh.** Each cache keeps its current refresh behavior and interval, run per partition. We can revisit them once they live in one place.
7. **Cache key.** Partition caches are keyed by partition ID only. Adding the FHIR service to the key later is a small change.
8. **Background work.** Code that runs outside a request (refresh timers, jobs) either calls `PartitionCache<T>` with a partition ID or creates a `FhirRequestContext` with `IsBackgroundTask = true` and the `PartitionId`, as background jobs do today.
9. **Rollout.** Three PRs, one group of caches at a time, as listed in the catalogue's rollout phases. Single-partition behavior stays unchanged throughout.

```
Core                                        SqlServer
Cache<T>            (singleton, per app)    SqlPartitionCache<T> : PartitionCache<T>
PartitionCache<T>   (singleton, per partition)   SqlIdMapCache, CustomQueryCache, …
ScopedCache<T>      (scoped, current partition)
CacheInitializer    (preloads all caches for all partitions)
```

Example: `GET <base-url>/partition/b/Patient?name=Smith` → the request context holds partition `b` → `SqlServerSearchService` injects `ScopedCache<SqlIdMaps>` → it returns `SqlIdMapCache`'s value for partition `b`.

## Status
Proposed

## Consequences
### Benefits
- A partition never uses another partition's cached data.
- One mechanism and one place to see what is cached and how it is loaded and refreshed.
- Each cache type stays separate and can be loaded, refreshed and later evicted independently.
- Existing synchronous callers don't need to become async.

### Adverse effects
- Large refactor: search parameters and SQL ID maps are each used in about 30 files. The phased rollout keeps each PR reviewable.
- Memory use grows with the number of partitions while everything is preloaded. Not measured yet.

### Neutral effects
- **One FHIR service per app for now.** Hosting several FHIR services in one app would key caches by FHIR service and partition, and also needs routing, configuration and connections per FHIR service.
- **Depends on PR #6017** for the partition ID in the request context.

## References
- [multitenancy-cache-catalogue.md](multitenancy-cache-catalogue.md)
- `adr-2610-multitenancy` (multi-tenancy design)
- [adr-2603-load-independent-search-param-cache-sync.md](adr-2603-load-independent-search-param-cache-sync.md) (search parameter cache sync across instances, which becomes per partition)
- Azure SDK sync/async pattern: [TaskExtensions.cs](https://github.com/Azure/azure-sdk-for-net/blob/515a1f7193a0f514dd0b4215f6f511977dd987bc/sdk/core/Azure.Core/src/Shared/TaskExtensions.cs#L27)

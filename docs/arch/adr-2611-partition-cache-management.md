# ADR 2611: Partition cache management

## Context
- With multi-tenancy (`adr-2610-multitenancy`), one app serves a FHIR service that contains several partitions. Each partition is an independent FHIR server with its own database, addressed as `<base-url>/partitions/<partition-name>`.
- The server keeps about 18 caches, listed in [multitenancy-cache-catalogue.md](multitenancy-cache-catalogue.md). Today each one is a singleton or static loaded from the single database, and each class has its own load and refresh logic.

## Decision
We will manage caches per partition through a cache manager in `Microsoft.Health.Fhir.Core`.

1. **One `CacheManager` per partition.** It owns every cache whose data comes from that partition's database ("Partition" level in the catalogue), loads it, and keeps it fresh.
2. **`PartitionManager` holds the cache managers.** It maps partition ID → `CacheManager` and creates one for each configured partition at startup.
3. **Shared caches are held once and passed in.** Global caches (built into the server, such as spec search parameters) and FHIR service caches (from configuration, such as roles) are built once into a `SharedCaches` object. The `PartitionManager` passes it to each `CacheManager` it creates, rather than each cache manager reading app-wide singletons. Each partition's `CacheManager` exposes them, so callers have a single entry point.
4. **All callers read through the cache manager.** Code gets the current partition's `CacheManager` and stops keeping its own copies or statics.
5. **Refresh moves into the cache manager.** Each cache keeps its current refresh behavior and timer, run per partition.
6. **Core stays storage-agnostic.** Core defines the cache manager and cache interfaces. SQL-only caches (SQL ID maps, custom query procedures, SQL statistics, `dbo.Parameters` values) are implemented in `Microsoft.Health.Fhir.SqlServer` and plugged in.
7. **Everything stays loaded.** Each partition's caches load at startup and stay in memory. No lazy loading or eviction for now.
8. **Background work selects its partition the way background jobs do today.** Jobs already create their own `FhirRequestContext` (with `IsBackgroundTask = true`) before running. Refresh timers and other partition work outside a request do the same and set its `PartitionId`, once per partition.
9. **Rolled out in three PRs**, one group of caches at a time, as listed in the catalogue's rollout phases. Single-partition behavior stays unchanged throughout.

```
App (serves one FHIR service)
├── SharedCaches ──────────── built once
│   ├── Global caches ──────── e.g. spec search parameters
│   └── FHIR service caches ── e.g. roles, key health
└── PartitionManager ──────── passes SharedCaches to each CacheManager
    ├── partition-a → CacheManager ── caches from database A + SharedCaches
    ├── partition-b → CacheManager ── caches from database B + SharedCaches
    └── …
```

Example: `GET <base-url>/partitions/partition-b/Patient?name=Smith` → the request context holds `partition-b` → search code gets partition-b's `CacheManager` → it uses partition-b's search parameters and SQL IDs.

## Status
Proposed

## Consequences
### Benefits
- A partition never uses another partition's cached data.
- One place to find, load and refresh cached data, instead of many classes.
- Built-in data such as spec search parameters is loaded once per app, not once per partition.
- **Works for both packing scenarios.** It serves one app running the partitions of one FHIR service today. One app running several FHIR services only needs a `FhirServiceManager` that builds one `SharedCaches` per FHIR service, and partitions keyed by FHIR service and partition name. `CacheManager` and its callers stay the same.

### Adverse effects
- Large refactor: search parameters and SQL ID maps are each used in about 30 files. The phased rollout keeps each PR reviewable.
- Memory use and background work grow with the number of partitions, since each has its own caches and refresh timers. Not measured yet.

### Neutral effects
- **Multiple FHIR services per app is not built now.** Beyond caches, it would also need routing by FHIR service, per-service configuration and security, and connections per SQL server.
- **Depends on PR #6017** for the partition ID in the request context.

## References
- [multitenancy-cache-catalogue.md](multitenancy-cache-catalogue.md)
- `adr-2610-multitenancy` (multi-tenancy design)
- [adr-2603-load-independent-search-param-cache-sync.md](adr-2603-load-independent-search-param-cache-sync.md) (search parameter cache sync across instances, which becomes per partition)

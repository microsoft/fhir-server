# ADR 2602: Search Parameter Definition & Binding Separation

*Labels*: [Core](https://github.com/microsoft/fhir-server/labels/Area-Core) | [SearchParameter](https://github.com/microsoft/fhir-server/labels/Area-SearchParameter)

---

## Context

### Problem Statement

The `SearchParameterDefinitionManager` maintained two in-memory lookup structures:

1. **`UrlLookup`** (`ConcurrentDictionary<string, SearchParameterInfo>`) — keyed by the search parameter URL, storing the canonical `SearchParameterInfo` instance.
2. **`TypeLookup`** (`ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentQueue<SearchParameterInfo>>>`) — keyed by resource type → code, storing queues of `SearchParameterInfo` objects directly.

Because `TypeLookup` stored full `SearchParameterInfo` object references independently from `UrlLookup`, several problems arose:

- **Duplicate object instances**: The same logical search parameter could be represented by different `SearchParameterInfo` objects in `UrlLookup` and `TypeLookup`. Mutations (e.g., status changes) applied to one copy would not be reflected in the other, causing silent inconsistencies.
- **Fragile deletion and status updates**: `DeleteSearchParameter` and `UpdateSearchParameterStatus` operated on `UrlLookup` by URL, then performed a separate, case-insensitive scan of `TypeLookup` to remove matching entries. This two-phase approach was susceptible to race conditions and partial failures.
- **Case-sensitivity inconsistency**: The `TypeLookup` removal path used `StringComparison.OrdinalIgnoreCase` while `UrlLookup` used the default comparer, allowing edge-case mismatches.
- **Duplicate status entries**: `SearchParameterStatusManager.EnsureInitializedAsync` converted status rows to a dictionary keyed by `Uri`, which would throw on duplicate keys if the status data store returned multiple rows for the same URI (a scenario possible after repeated re-indexing operations).

### Architecture Context

These in-memory structures are shared across all request-handling threads in a FHIR server instance. They are populated at startup from the embedded FHIR specification bundle and then augmented at runtime when custom search parameters are created, updated, or deleted. Consistency of these structures is critical because:

- **Search execution** resolves parameter definitions via `TypeLookup` to compile FHIRPath expressions.
- **Reindex jobs** rely on the hash of search parameters per resource type; a stale or duplicated entry changes the hash and triggers unnecessary reindexing.
- **Search parameter status management** reads and writes statuses that must map 1-to-1 with the in-memory definitions.
- **Multi-instance deployments** (blue/green, auto-scale) depend on deterministic startup so every instance converges to the same in-memory state from the same specification bundle.

### Design Assumption: URL Uniqueness

We assume that each search parameter URL uniquely identifies a single definition. The FHIR specification treats search parameter URLs as canonical identifiers, and the FHIR server enforces uniqueness when custom search parameters are created or updated. Consequently, the design uses the plain URL string as the identity key in both `UrlLookup` and `TypeLookup`, avoiding the complexity of a composite key.

## Decision

We will **separate search parameter definition storage from resource-type binding** by introducing an indirection layer: `TypeLookup` stores URL strings that reference entries in the authoritative `UrlLookup` dictionary, rather than holding `SearchParameterInfo` objects directly.

### 1. URL as Identity Key

`UrlLookup` remains keyed by the search parameter's URL string (`searchParameter.Url.OriginalString`). Since URLs are guaranteed unique, this is a sufficient and performant identity. No composite key or secondary identity scheme is needed.

### 2. Retyped Binding Dictionary

| Before | After |
|--------|-------|
| `TypeLookup`: `ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentQueue<SearchParameterInfo>>>` | `TypeLookup`: `ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentQueue<string>>>` |

The property name remains `TypeLookup`. The inner queues now contain URL strings instead of `SearchParameterInfo` references. All reads resolve through `UrlLookup` via direct `O(1)` dictionary lookup to obtain the canonical object instance.

### 3. Eager Registration in `UrlLookup`

`GetOrCreateSearchParameterInfo` in `SearchParameterDefinitionBuilder` now registers every `SearchParameterInfo` into `uriDictionary` (which becomes `UrlLookup`) at creation time using the URL as key. If a definition with the same URL already exists, the existing instance is returned (first-writer-wins). This eliminates the previous pattern where a new `SearchParameterInfo` could be created but never added to `UrlLookup`, resulting in the object in `TypeLookup` diverging from the one in `UrlLookup`.

### 4. `TryGetFromTypeLookup` Helper

A new private method `TryGetFromTypeLookup(resourceType, code, out SearchParameterInfo)` centralizes the two-step resolution pattern: look up the URL queue in `TypeLookup`, then resolve the first valid URL through `UrlLookup`. All `GetSearchParameter` and `TryGetSearchParameter` overloads delegate to this helper, eliminating duplicated resolution logic.

### 5. Direct URL Lookups

With URL-keyed storage, all lookups are direct `O(1)` dictionary operations:

| Operation | Implementation |
|-----------|---------------|
| Get by URL | `UrlLookup.TryGetValue(url, out var sp)` |
| Delete by URL | `UrlLookup.TryRemove(url, out var sp)` |
| Resolve binding | `TryGetFromTypeLookup` iterates the `TypeLookup` queue, calling `UrlLookup.TryGetValue(url, ...)` for each entry |

No linear scans of `UrlLookup.Values` are required. No ambiguity checks are needed since URL uniqueness is guaranteed.

### 6. Merged `Build` Overloads

The two `Build` method overloads in `SearchParameterDefinitionBuilder` — one thin wrapper that forwarded with `isSystemDefined: false` and one full implementation — have been consolidated into a single method with `bool isSystemDefined = false` as a default parameter.

### 7. Authoritative `_type` Search Parameter Registration

The `_type` (`ResourceType`) search parameter is absent from the STU3 and R4 embedded FHIR bundles but present in R4B and R5. The R5 bundle declares `_type` with type `"special"`, which the `SearchParamType` enum parses to `Special` — different from the authoritative static definition (`SearchParameterInfo.ResourceTypeSearchParameter`) which uses `Token`. To prevent type mismatches, `ValidateAndGetFlattenedList` **unconditionally** writes the static `ResourceTypeSearchParameter` into `uriDictionary`, overwriting any bundle-parsed entry. This guarantees the `Token`-typed definition is always the single source of truth for `_type` across all FHIR versions.

### 8. Case-Sensitive URL Matching

All URL comparisons in deletion (`DeleteSearchParameter`) and status-dictionary keying now use `StringComparison.Ordinal` / `StringComparer.Ordinal`, replacing the previous `OrdinalIgnoreCase`. This aligns with the FHIR specification, which treats URLs as case-sensitive identifiers.

### 9. `SearchParameterInfo.GetHashCode` Consistency Fix

`SearchParameterInfo.Equals` compares only by `Url` when both URLs are non-null, but `GetHashCode` previously included `Type`, `Expression`, and `SearchParameterStatus` — violating the `Object.GetHashCode` contract. Two objects with the same URL but different types (e.g., the bundle-parsed `_type` with `Special` and the static `_type` with `Token`) would be `Equals=true` but produce different hash codes, causing `HashSet<SearchParameterInfo>` to retain both as distinct entries. `GetHashCode` now returns `Url.GetHashCode()` when `Url` is non-null, and falls back to `HashCode.Combine(Code, Type, Expression)` only when `Url` is null — matching the fields used by `Equals` in each case.

### Key Files Modified

| File | Change Summary |
|------|---------------|
| `SearchParameterDefinitionBuilder.cs` | Merged two `Build` overloads into one with a default parameter. Changed `resourceTypeDictionary` inner queues from `ConcurrentQueue<SearchParameterInfo>` to `ConcurrentQueue<string>` (URL strings). `GetOrCreateSearchParameterInfo` eagerly registers definitions in `uriDictionary`. `ValidateAndGetFlattenedList` ensures `_type` URL is registered for STU3/R4 compatibility. `BuildSearchParameterDefinition` now accepts `uriDictionary` and resolves URL strings through it for sorting and deduplication. |
| `SearchParameterDefinitionManager.cs` | `TypeLookup` retyped to store `ConcurrentQueue<string>` (URL strings) instead of `ConcurrentQueue<SearchParameterInfo>`. Added `TryGetFromTypeLookup` private helper. All query methods (`GetSearchParameters`, `GetSearchParameter`, `TryGetSearchParameter`) resolve bindings through `UrlLookup` via direct `TryGetValue`. Deletion uses case-sensitive (`StringComparison.Ordinal`) URL matching. Hash calculation delegates to `GetSearchParameters` for consistent resolution. |
| `SearchParameterDefinitionManagerExtensions.cs` | `GetSearchParametersByResourceTypes` flattens `TypeLookup` URL queues and resolves through `UrlLookup`. `GetSearchParametersByUrls` uses direct `O(1)` key lookups from input URLs instead of scanning `UrlLookup.Values`. |
| `SearchParameterStatusManager.cs` | Changed `parameters` dictionary to use `string` keys (URL original string) with `StringComparer.Ordinal`, aligning with the string-keyed design used throughout the refactoring. |
| `search-parameters.json` (R5) | Reverted the `_type` search parameter's `type` field back to `"special"` (the value parseable by the `SearchParamType` enum). The R5 spec uses `"type"` but there is no corresponding enum literal; unparseable values silently default to `Number`, causing type mismatches. The static `ResourceTypeSearchParameter` definition (Token) is authoritative regardless of the bundle value. |
| `SearchParameterInfo.cs` | Fixed `GetHashCode`/`Equals` contract violation: `GetHashCode` now uses only `Url` when `Url` is non-null, matching `Equals` behavior. Prevents duplicate entries in `HashSet<SearchParameterInfo>` for objects with the same URL but different `Type` or `Expression`. |
| `SearchConverterForAllSearchTypes.cs` | Added `continue` skip for `_type` code in the unsupported-parameters comparison loop, and changed component expression resolution to use `GetSearchParameter` instead of direct `UrlLookup` indexer access. |
| Unit test files | Updated `ConcurrentQueue<SearchParameterInfo>` to `ConcurrentQueue<string>` throughout. Added tests for: case-variant URI resolution, `TypeLookup` → `UrlLookup` binding resolution, R5 `_type` token definition, existing-URL preservation (first-writer-wins), and search parameter conflict resolution with URL-keyed queues. |

## Status

**Accepted**

## Consequences

### Positive

- **Single source of truth**: Every `SearchParameterInfo` object exists exactly once in `UrlLookup`. Status mutations, enable/disable toggles, and PendingDelete flags are immediately visible to all code paths that resolve through the binding layer.
- **O(1) lookups everywhere**: Because `UrlLookup` is keyed by URL and `TypeLookup` stores URL strings, all resolutions are direct dictionary hits with no linear scans.
- **Simpler identity model**: No composite key generation, no key format to maintain, no risk of key drift. The URL *is* the identity.
- **Safer concurrent operations**: Eager registration in `UrlLookup` at creation time eliminates the window where a definition could be built but not yet registered, reducing the surface area for race conditions documented in ADR 2512.
- **Resilient status initialization**: Duplicate status rows no longer cause startup crashes; the highest-priority status wins deterministically.
- **Case-sensitive URL matching**: Aligns with the FHIR specification, which treats URLs as case-sensitive identifiers.
- **Centralized resolution logic**: The `TryGetFromTypeLookup` helper eliminates duplicated two-step resolution code across multiple `GetSearchParameter`/`TryGetSearchParameter` overloads.

### Negative

- **Indirection cost**: Every `TypeLookup` read requires a secondary `O(1)` lookup in `UrlLookup` to resolve the URL string to a `SearchParameterInfo`. This is a trivial overhead since both dictionaries are in-memory.
- **URL round-trip in `BuildSearchParameterDefinition`**: When building sorted queues during the `AddOrUpdate` lambda, existing URL strings must be resolved back to `SearchParameterInfo` objects for expression-based sorting, then converted back to URL strings. This is an inherent cost of the URL-keyed design that occurs only at startup during bundle processing.

### Edge Cases and Risks

- **Stale binding URLs**: If a definition is removed from `UrlLookup` but its URL remains in `TypeLookup`, the resolution will skip it gracefully (the `Where(uri => UrlLookup.TryGetValue(...))` filter). Stale URLs accumulate until the next full rebuild. This is acceptable for the current lifecycle where deletions are infrequent.
- **URL uniqueness violation**: If a future scenario introduces definitions that share a URL but differ in other properties (e.g., a FHIR version migration), the URL-keyed design would silently keep the first-written instance. This is mitigated by the FHIR server's existing uniqueness enforcement on SearchParameter creation. If this constraint were ever relaxed, a composite key or secondary index would need to be introduced.
- **`_type` bundle coverage gap**: The `_type` search parameter is absent from STU3 and R4 embedded bundles but present in R4B (`"token"`) and R5 (`"special"`). The unconditional overwrite in `ValidateAndGetFlattenedList` ensures the static `Token`-typed definition is always used, regardless of whether the bundle includes `_type` or what type value it declares.
- **`SearchParamType` enum gap**: The R5 FHIR specification defines a `"type"` search parameter type that has no corresponding `SearchParamType` enum literal. `EnumUtility.ParseLiteral("type")` returns `null`, and `.GetValueOrDefault()` silently falls back to `Number` (enum value 0). The R5 bundle's `_type` entry was reverted to `"special"` to avoid this silent misparse. If a `Type` enum value is added in the future, the bundle can be updated and the unconditional overwrite logic revisited.
- **Backward compatibility**: The `TypeLookup` property is `internal`, so there is no public API change. The blast radius is limited to the FHIR server's own assemblies and unit tests.
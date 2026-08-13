# ADR 2607: Incremental Ignixa SDK Migration — Phase 0 ($import)

## Context

The server is migrating from the Firely FHIR SDK to Ignixa, a new FHIR SDK offering faster FHIRPath evaluation and serialization. An earlier full attempt (`feature/ignixa-sdk`, later extended in `personal/bkowitz/ignixa-sdk-next-steps-fable`) wired Ignixa natively across import, persistence, HTTP formatters, and validation in a single large change — 37 new/modified files in the Ignixa-specific surface alone, more once formatter and feature-flag wiring is included. That scope is too large for one reviewable PR and creates a single point of failure if any one seam has a problem.

We need a migration strategy that lands real, mergeable progress in small increments without destabilizing the default (Firely) request path, and without accumulating a facade or abstraction layer that outlives its usefulness once Firely is eventually removed.

## Decision

Integrate Ignixa one feature seam at a time behind a global two-state provider setting:

```csharp
public enum FhirSdkProvider
{
    Firely = 0,
    Ignixa = 1,
}
```

- `CoreFeatureConfiguration.FhirSdkProvider` defaults to `Firely`. There is no `Hybrid` mode and no runtime fallback from Ignixa to Firely — shadow comparison is a testing technique, not a production mode.
- Selecting `Ignixa` means every feature seam already migrated uses its Ignixa implementation; every other seam keeps using Firely until that seam is migrated in its own PR.
- Startup logs the configured provider and the seams it currently controls (`FhirSdkProviderStartupLogger`: `"FHIR SDK provider configured: {FhirSdkProvider}; migrated seams: Import."`), so the global setting never creates a false impression that the whole server has moved.
- We do not introduce an `IFhirSdkProvider` facade — it would accumulate unrelated serialization, validation, FHIRPath, and persistence responsibilities. Each migrated feature keeps its existing narrow contract (Phase 0 reuses `IImportResourceParser` unchanged) or introduces one narrow contract if none exists.

**Phase 0 migrates only `$import` parsing.**

- Four version-specific Firely provider projects (`Microsoft.Health.Fhir.{Stu3,R4,R4B,R5}.FirelySdk`) share one `FirelyImportResourceParser` source file so behavior can't drift by version.
- One `Microsoft.Health.Fhir.Ignixa` project carries all Ignixa code and targets `net10.0`, matching the repo's single target framework (`Directory.Build.props`, `net10.0` since .NET 8 build targets were retired in #5686). The pinned package version (`IgnixaPackageVersion` = `0.0.163` in `Directory.Packages.props`) ships only `net9.0` binaries, which a `net10.0` project consumes fine through ordinary NuGet TFM compatibility; newer Ignixa releases (`0.6.4`, seen in the local package cache) already ship a `net10.0` target directly.
- `OperationsModule` registers exactly one `IImportResourceParser` from the configured provider at startup; it never resolves both parsers per resource or catches an Ignixa failure to retry with Firely.

The Ignixa parser intentionally converts its parsed node to the existing Firely-shaped `ResourceElement` before calling the existing `IResourceWrapperFactory`, rather than preserving the native Ignixa node end-to-end. This keeps Phase 0 at the ~10-file guardrail (see below) and leaves the entire downstream pipeline (search indexing, raw-resource creation, storage) unchanged for either provider. The deliberate cost: `RawResourceFactory` still rebuilds a full Firely POCO and serializes through Firely's `FhirJsonSerializer` regardless of which parser produced the resource, so Ignixa mode is performance-neutral-to-slightly-slower and higher-allocating than Firely mode on `$import` today. Recovering that win is Phase 2a below (the write-side persistence codec), which is scheduled immediately after Phase 0 for exactly that reason — see Adverse Effects and Execution order.

Within the parser, soft-delete detection and removal use genuinely native Ignixa APIs rather than a Firely adapter or raw-JSON code:

- Soft-delete detection evaluates the same `Resource.meta.extension...` predicate the Firely parser runs via `ResourceElement.IsSoftDeleted()`, but directly against the native `IElement` through `Ignixa.FhirPath`'s `IElement.Predicate(path, EvaluationContext)`. This is a deliberate, narrow exception to the "no FHIRPath in Phase 0" scope guideline — one predicate, fully contained inside the import parser. Search-parameter extraction for indexing is unaffected: it still runs through Firely's engine over the Firely-shaped `ResourceElement`, and stays scoped to Phase 3 below. `Ignixa.FhirPath` is consequently a genuinely new package dependency, scoped to `Microsoft.Health.Fhir.Ignixa.csproj` only.
- The matched extension is removed through the typed `SourceNodeExtensions.RemoveExtension(MetaJsonNode, url)` helper rather than manual `JsonObject`/`JsonArray` traversal — verified empirically that it removes only one match per call (looped to mirror Firely's `Meta.RemoveExtension`, which removes every match), and that `ResourceJsonNode` caches its converted `IElement` per instance (mutating the node requires `InvalidateCaches()` before the next `ToElement()` call, or it silently returns the stale element).

Both providers must preserve the current import policy exactly:

- Valid resource ID required; conditional references rejected on initial load, allowed on incremental load.
- `meta` initialized when absent; `lastUpdated` normalized to milliseconds and rejected if in the future.
- Version preserved when valid, otherwise reset to `1`; soft-deleted resources detected and their extension stripped before persistence.
- Conditional-reference detection — the highest-risk behavior in the Ignixa parser — covers the resource's own schema-declared reference fields (`IFhirSchemaProvider.ReferenceMetadataProvider`) with the same semantics as Firely's `GetAllChildren<ResourceReference>()`, each read through the typed `ReferenceJsonNode` model. This is scoped to what the search indexer itself already treats as in scope (see Neutral Effects), and intentionally strict about malformed shapes: a `reference` property holding a non-string scalar throws, and a reference field that is present but isn't a JSON object at all (confirmed empirically that `resource.ToElement(schema)` does not reject this shape on its own) also throws, rather than either case being silently skipped. A missing `reference` property (identifier-only or display-only references, both valid FHIR) is correctly treated as "no conditional reference" rather than dereferenced — a real `NullReferenceException` here was caught and fixed during review.

### Migration ladder (subsequent PRs, one seam each)

1. **Export NDJSON serialization** — switch `IResourceToByteArraySerializer`; byte/semantic parity corpus across all versions; no formatter or persistence changes.
2. **Persistence codecs** (two PRs) — **2a, writes:** provider-selected `IRawResourceFactory` with a native-serialize branch. This is what recovers Phase 0's deferred performance win. **2b, reads:** provider-selected `IResourceDeserializer`, preserving the native node after a database read. Both halves must continue returning existing Core types to SQL and Cosmos, prove parity for raw JSON, search values, history, and deleted resources, and keep rollback possible without rewriting stored data.
3. **FHIRPath and search indexing** (three PRs) — provider-neutral FHIRPath evaluation context, a Firely-authoritative parity corpus per generated search parameter per version, then switch indexing and reindex together (they must use the same provider to avoid index drift; `resolve()` behavior is a release blocker).
4. **Ordinary HTTP JSON ingress** — single-resource create/update only; Bundles, Parameters, JSON/FHIRPath Patch, and XML explicitly excluded.
5. **HTTP JSON egress** (three PRs) — single-resource responses, then search/history bundles, then `_summary`/`_elements` projection.
6. **Validation** (three PRs) — primitive/structural, then conformance-resource, then profile/terminology-backed. Ignixa success must never synchronously invoke Firely validation as a check.
7. **Complex write semantics** — one operation family per PR (conditional-reference mutation, transaction/batch bundles, JSON Patch, FHIRPath Patch, bulk update/codecs).
8. **Remaining surfaces** — CapabilityStatement/conformance, terminology, resource-parser tools, XML (XML needs an explicit retain/replace/remove decision; Firely can't be deleted while supported XML behavior depends on it).
9. **Firely removal** — only after an inventory shows zero remaining Firely runtime seams: delete the four Firely provider projects, remove Firely packages/adapters, remove provider-selection code, remove now-unneeded compatibility conversions.

### Execution order

The ladder above is a *dependency* order, not a commitment to execute it top to bottom.

The first delivery target is a demonstrable `$import` performance win reachable through a supported configuration change — the benefit measured on the throwaway `feature/ignixa-sdk` branch, reproduced on the production toggle path. That is a vertical slice of **Phase 0 + Phase 2a**, and it does not require Phase 1 or Phase 2b: `$import` is a write path, so `ResourceWrapperFactory` calls `IRawResourceFactory` on every imported resource, while `IResourceDeserializer` is not on the import path at all.

Phase 2a is therefore scheduled ahead of both Phase 1 and Phase 2b, followed by a benchmark gate comparing Ignixa and Firely modes on one binary with only configuration differing. If that gate does not reproduce the `feature/ignixa-sdk` delta, the bottleneck is identified and written up before the ladder resumes. Phase 1 and Phase 2b follow the gate.

The gate has since been met. `test/Microsoft.Health.Fhir.R4.Benchmarks` measures the three per-resource costs on one binary (ShortRun, in-process toolchain, R4 `Patient` with three names, two telecoms, four identifiers and two references):

| Stage | Firely | Ignixa | Speedup | Allocated (Firely → Ignixa) |
|---|---|---|---|---|
| Parse | 107.3 µs | 10.7 µs | 10.0× | 171.7 KB → 6.3 KB |
| Serialize raw resource | 72.5 µs | 3.5 µs | 20.7× | 107.7 KB → 2.2 KB |
| Evaluate search expressions | 54.2 µs | 36.3 µs | 1.5× | 89.8 KB → 65.1 KB |

Together that is roughly 234 µs and 369 KB per resource under Firely against 51 µs and 74 KB under Ignixa. Serialization gains the most because the Firely path rebuilds a POCO before writing, while the Ignixa path writes the document it already parsed; FHIRPath gains least because the search value converters that consume the results still run on Firely.

Phase numbering stays fixed regardless of execution order — the backlog references these numbers, so they are identifiers, not a schedule.

### Amendment: Phase 2a and Phase 3a delivered together

Phase 2a (write-side serialization) and the indexing half of Phase 3 shipped as a single change rather than as separate seam PRs. The seams cannot be split without one of them being unverifiable: `ResourceWrapperFactory.Create` calls `IRawResourceFactory.Create` and `ISearchIndexer.Extract` on every imported resource, and those two calls are the entire per-resource cost of `$import`. Migrating either alone leaves a full Firely POCO rebuild on the path, so neither the SDK nor the performance claim can be demonstrated. This deliberately exceeds the ten-file review heuristic; the heuristic asks for that to be explained rather than forbidding it.

Three findings from that work correct the plan recorded above.

**The native node needs no carrier, and the two-argument `ResourceElement` constructor could not have been one.** That constructor's `ResourceInstance` slot is a Firely POCO cache which `ModelExtensions.ToPoco<T>()` reads with a hard `(T)` cast, so an Ignixa node placed there raises `InvalidCastException` for every caller. No carrier is required in any case: the import parser builds its `ResourceElement` from `IElement.ToTypedElement()`, and the reverse conversion `ToIgnixaElement()` recognises that adapter and returns the *same* native instance, so imported resources reach the Ignixa FHIRPath engine with no conversion. The live `System.Text.Json` document is reachable the same way, which is what the raw resource factory serializes. Elements from anywhere else convert through a lazy adapter, and expose no JSON document — the structural signal that the raw resource factory must defer to Firely. `ResourceElement` is unchanged.

**Byte parity with Firely required bypassing Ignixa's own serializer.** `JsonSourceNodeFactory.SerializeToString` uses `System.Text.Json`'s default escaper, which encodes `+`, `&`, `<`, `>`, `'` and all non-ASCII — a `lastUpdated` offset would persist as `2024-01-02T03:04:05.678\u002B00:00`. Because the raw resource is stored and returned to clients verbatim, the factory writes the document itself with `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` and normalises `meta.lastUpdated`, whose typed setter emits seven fractional digits where Firely emits at most milliseconds and trims trailing zeros. With both corrections the two providers produce byte-identical raw resources across timezone offsets, non-ASCII text, XHTML narrative and decimal precision. One documented difference remains: `meta` created for a resource that had none is appended last rather than placed after `id`, which is a property-order difference in an equivalent document.

**FHIRPath system types are mapped back to Firely's naming.** FHIRPath defines function results as system types; Firely reports `System.Boolean`/`System.Integer` where Ignixa reports the FHIR primitive name. This does not change which search value converter the indexer selects today, because the converters register both spellings — `BooleanToTokenSearchValueConverter` declares `"boolean", "System.Boolean"`, and the integer, decimal, string, code, date and quantity converters do likewise. The mapping is applied anyway so that provider parity is a property of the seam rather than a coincidence of that registration table: a converter added later declaring only one spelling would otherwise silently index different values depending on the configured provider, and the parity tests can assert `InstanceType` equality outright instead of the weaker "both names happen to resolve to the same converter". The map is restricted to system type names the converters actually recognise, and every entry is covered by a test asserting Firely reports the same name for an equivalent computed expression; mapping a primitive to a name Firely does not itself produce would manufacture the divergence the adapter exists to prevent. Computed values are identified by carrying no schema type.

**Thread safety of the shared Ignixa engine is asserted, not assumed.** `IFhirPathEvaluator` is a singleton holding one `FhirPathParser`, `FhirPathEvaluator` and `FhirPathDelegateCompiler`, and `ConcurrentDictionary.GetOrAdd` does not serialise its value factory, so both compilation and evaluation genuinely run those shared objects in parallel under import. The Ignixa package does not document whether that is supported, and indexing has no runtime fallback, so a race would appear as intermittent index corruption rather than a clean failure. `IgnixaFhirPathEvaluatorConcurrencyTests` drives sixteen threads over distinct resources and compares every result against the single-threaded Firely result, and covers the cold-cache compile path separately.

Every migration PR changes exactly one feature seam, preserves Firely as the default until final cutover, has one composition-root decision point, avoids hidden fallback, states its rollback action (typically: reset the provider setting; no data migration required), and states which seams remain Firely-backed. As a review heuristic, we target no more than roughly ten modified production files per seam PR (excluding new provider-project scaffolding and mechanical solution/Docker entries) — exceeding that isn't automatically wrong, but it requires explaining why the seam can't be split further. Phase 0 lands at exactly ten.

## Status

Accepted

## Consequences

### Benefits

- Each seam lands as an independently reviewable, independently revertible PR instead of one large cutover.
- Firely stays the default and fully functional throughout the migration; rollback at any point is a configuration change, not a data migration.
- Reusing existing narrow contracts (`IImportResourceParser`, `IResourceWrapperFactory`) means downstream consumers (indexing, storage, job processing) require zero changes for Phase 0, and the parity test suite can assert byte-level equivalence between providers.
- The migration ladder gives reviewers a shared map of what's left, preventing "is this seam actually migrated?" ambiguity.
- The parser is a worked example of idiomatic native Ignixa usage (`IElement.Predicate`, `MetaJsonNode`/`ReferenceJsonNode`, `InvalidateCaches()`) for later migration-ladder PRs to build on.

### Adverse Effects

- Phase 0 delivers no performance improvement for Ignixa-mode `$import` — likely a slight regression versus Firely mode, from paying both an Ignixa parse and a full Firely POCO rebuild + serialize. Call this out explicitly wherever the migration's performance rationale is cited, so reviewers and operators don't assume Phase 0 alone delivers the documented Ignixa speedup; recovering it is Phase 2a (the write-side persistence codec), which has since shipped together with indexing — see the amendment under Execution order.
- The Ignixa→Firely `ResourceElement` conversion is a known, intentional shim. Phase 2a has since removed the cost it deferred: the adapter round trip is recognised by the SDK and unwraps to the original native instance, so no POCO is rebuilt on the `$import` path. See the amendment under Execution order.
- A two-state provider enum will need reconciling later if a `Hybrid`/shadow-comparison mode is ever wanted for a specific seam — deliberately excluded from Phase 0, not a limitation of the enum shape.

### Neutral Effects

- Conditional-reference checking in Ignixa mode does not recurse into `contained` resources or Bundle entries. This matches the search indexer's existing behavior (which also never indexes into `contained`) and import's NDJSON-of-individual-resources model — an intentional scope boundary, not a parity gap to close later.

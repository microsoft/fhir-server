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
- One `Microsoft.Health.Fhir.Ignixa` project targets `net10.0` only. The repo builds `net10.0;net8.0` (`Directory.Build.props`); configuring `Ignixa` on net8 fails at startup with a clear error rather than silently degrading. This is a version-pin fact, not an inherent Ignixa limitation: the pinned package version (`IgnixaPackageVersion` = `0.0.163` in `Directory.Packages.props`) ships only `net9.0` binaries, which a `net10.0` project consumes fine through ordinary NuGet TFM compatibility; newer Ignixa releases (`0.6.4`, seen in the local package cache) already ship a `net10.0` target directly.
- `OperationsModule` registers exactly one `IImportResourceParser` from the configured provider at startup; it never resolves both parsers per resource or catches an Ignixa failure to retry with Firely.

The Ignixa parser intentionally converts its parsed node to the existing Firely-shaped `ResourceElement` before calling the existing `IResourceWrapperFactory`, rather than preserving the native Ignixa node end-to-end. This keeps Phase 0 at the ~10-file guardrail (see below) and leaves the entire downstream pipeline (search indexing, raw-resource creation, storage) unchanged for either provider. The deliberate cost: `RawResourceFactory` still rebuilds a full Firely POCO and serializes through Firely's `FhirJsonSerializer` regardless of which parser produced the resource, so Ignixa mode is performance-neutral-to-slightly-slower and higher-allocating than Firely mode on `$import` today. Recovering that win is Phase 2 below (persistence codecs) — see Adverse Effects.

Within the parser, soft-delete detection and removal use genuinely native Ignixa APIs rather than a Firely adapter or raw-JSON code:

- Soft-delete detection evaluates the same `Resource.meta.extension...` predicate the Firely parser runs via `ResourceElement.IsSoftDeleted()`, but directly against the native `IElement` through `Ignixa.FhirPath`'s `IElement.Predicate(path, EvaluationContext)`. This is a deliberate, narrow exception to the "no FHIRPath in Phase 0" scope guideline — one predicate, fully contained inside the import parser. Search-parameter extraction for indexing is unaffected: it still runs through Firely's engine over the Firely-shaped `ResourceElement`, and stays scoped to Phase 3 below. `Ignixa.FhirPath` is consequently a genuinely new package dependency, scoped to `Microsoft.Health.Fhir.Ignixa.csproj` only.
- The matched extension is removed through the typed `SourceNodeExtensions.RemoveExtension(MetaJsonNode, url)` helper rather than manual `JsonObject`/`JsonArray` traversal — verified empirically that it removes only one match per call (looped to mirror Firely's `Meta.RemoveExtension`, which removes every match), and that `ResourceJsonNode` caches its converted `IElement` per instance (mutating the node requires `InvalidateCaches()` before the next `ToElement()` call, or it silently returns the stale element).

Both providers must preserve the current import policy exactly:

- Valid resource ID required; conditional references rejected on initial load, allowed on incremental load.
- `meta` initialized when absent; `lastUpdated` normalized to milliseconds and rejected if in the future.
- Version preserved when valid, otherwise reset to `1`; soft-deleted resources detected and their extension stripped before persistence.
- Conditional-reference detection — the highest-risk behavior in the Ignixa parser — covers the resource's own schema-declared reference fields (`IFhirSchemaProvider.ReferenceMetadataProvider`) with the same semantics as Firely's `GetAllChildren<ResourceReference>()`, each read through the typed `ReferenceJsonNode` model. This is scoped to what the search indexer itself already treats as in scope (see Neutral Effects) and intentionally strict: a `reference` property holding a non-string value throws rather than being silently skipped.

### Migration ladder (subsequent PRs, one seam each)

1. **Export NDJSON serialization** — switch `IResourceToByteArraySerializer`; byte/semantic parity corpus across all versions; no formatter or persistence changes.
2. **Persistence codecs** (two PRs) — provider-selected `IResourceDeserializer` for reads, then provider-selected `IRawResourceFactory` for writes, preserving the native Ignixa node after a database read. Once `ResourceElement` carries that node (via its existing internal two-arg constructor) and `RawResourceFactory` has a native-serialize branch, this is what recovers Phase 0's deferred performance win.
3. **FHIRPath and search indexing** (three PRs) — provider-neutral FHIRPath evaluation context, a Firely-authoritative parity corpus per generated search parameter per version, then switch indexing and reindex together (they must use the same provider to avoid index drift; `resolve()` behavior is a release blocker).
4. **Ordinary HTTP JSON ingress** — single-resource create/update only; Bundles, Parameters, JSON/FHIRPath Patch, and XML explicitly excluded.
5. **HTTP JSON egress** (three PRs) — single-resource responses, then search/history bundles, then `_summary`/`_elements` projection.
6. **Validation** (three PRs) — primitive/structural, then conformance-resource, then profile/terminology-backed. Ignixa success must never synchronously invoke Firely validation as a check.
7. **Complex write semantics** — one operation family per PR (conditional-reference mutation, transaction/batch bundles, JSON Patch, FHIRPath Patch, bulk update/codecs).
8. **Remaining surfaces** — CapabilityStatement/conformance, terminology, resource-parser tools, XML (XML needs an explicit retain/replace/remove decision; Firely can't be deleted while supported XML behavior depends on it).
9. **Firely removal** — only after an inventory shows zero remaining Firely runtime seams: delete the four Firely provider projects, remove Firely packages/adapters, remove provider-selection code, remove now-unneeded compatibility conversions.

Every migration PR changes exactly one feature seam, preserves Firely as the default until final cutover, has one composition-root decision point, avoids hidden fallback, states its rollback action (typically: reset the provider setting; no data migration required), and states which seams remain Firely-backed. As a review heuristic, we target no more than roughly ten modified production files per seam PR (excluding new provider-project scaffolding and mechanical solution/Docker entries) — exceeding that isn't automatically wrong, but it requires explaining why the seam can't be split further. Phase 0 lands at exactly ten.

## Status

Accepted

## Consequences

### Benefits

- Each seam lands as an independently reviewable, independently revertible PR instead of one large cutover.
- Firely stays the default and fully functional on both net10 and net8 throughout the migration; rollback at any point is a configuration change, not a data migration.
- Reusing existing narrow contracts (`IImportResourceParser`, `IResourceWrapperFactory`) means downstream consumers (indexing, storage, job processing) require zero changes for Phase 0, and the parity test suite can assert byte-level equivalence between providers.
- The migration ladder gives reviewers a shared map of what's left, preventing "is this seam actually migrated?" ambiguity.
- The parser is a worked example of idiomatic native Ignixa usage (`IElement.Predicate`, `MetaJsonNode`/`ReferenceJsonNode`, `InvalidateCaches()`) for later migration-ladder PRs to build on.

### Adverse Effects

- Phase 0 delivers no performance improvement for Ignixa-mode `$import` — likely a slight regression versus Firely mode, from paying both an Ignixa parse and a full Firely POCO rebuild + serialize. Call this out explicitly wherever the migration's performance rationale is cited, so reviewers and operators don't assume Phase 0 alone delivers the documented Ignixa speedup; recovering it is Phase 2 (persistence codecs).
- The Ignixa→Firely `ResourceElement` conversion is a known, intentional shim. The line where the native node is dropped (the one-argument `ResourceElement` constructor, which leaves `ResourceInstance` unset) is marked in code as the Phase 2 flip point, so it's a planned one-line change plus a new native-serialize decorator, not a rediscovered defect.
- A two-state provider enum will need reconciling later if a `Hybrid`/shadow-comparison mode is ever wanted for a specific seam — deliberately excluded from Phase 0, not a limitation of the enum shape.

### Neutral Effects

- The Ignixa project's `net10.0`-only targeting doesn't remove net8 support anywhere else; any seam needing net8 Ignixa support later requires either an upstream package change or a decision to drop net8 entirely, out of scope here.
- Conditional-reference checking in Ignixa mode does not recurse into `contained` resources or Bundle entries. This matches the search indexer's existing behavior (which also never indexes into `contained`) and import's NDJSON-of-individual-resources model — an intentional scope boundary, not a parity gap to close later.

# Incremental Ignixa Integration Design

## Status

Proposed implementation design for review.

## Decision Summary

Integrate Ignixa one feature seam at a time behind a global two-state SDK provider setting. Phase 0 migrates only `$import`. It introduces four version-specific Firely provider projects and one multi-version Ignixa provider project, while preserving the existing `ImportResource` and `ResourceWrapper` boundary.

The default remains Firely. There is no Hybrid mode and no runtime fallback from Ignixa to Firely. Selecting Ignixa means that every feature seam already migrated to Ignixa uses its Ignixa implementation; all other seams continue to use their existing Firely implementation until a later, separately reviewed change.

## Goals

- Make the first Ignixa change small enough to review as one feature PR.
- Preserve current behavior by default.
- Keep SQL, Cosmos, import orchestration, indexing, and job processing unchanged in Phase 0.
- Support STU3, R4, R4B, and R5.
- Preserve both `net8.0` and `net9.0` builds.
- Establish provider boundaries that make deleting Firely straightforward later.
- Require semantic parity at each seam before that seam consumes the global provider setting.

## Non-Goals

Phase 0 does not change:

- HTTP JSON or XML formatters.
- Request binding or response serialization.
- Persistence deserialization or raw-resource creation.
- FHIRPath evaluation or search indexing.
- Validation.
- Bundle, transaction, conditional-reference, PATCH, or bulk-update behavior outside `$import`.
- CapabilityStatement or terminology behavior.
- Firely package versions.
- Target frameworks, CI topology, AAD setup, SQL behavior, or unrelated dependencies.

## Architecture

### Configuration

Add a two-state enum in Core:

```csharp
public enum FhirSdkProvider
{
    Firely = 0,
    Ignixa = 1,
}
```

Add `FhirSdkProvider` to `CoreFeatureConfiguration`, defaulting to `Firely`.

This is a global provider preference, not a claim that every server surface has migrated. Startup logging must state both the configured provider and the seams currently controlled by it:

```text
FhirSdkProvider=Ignixa; MigratedSeams=Import
```

Do not add `Hybrid`. Shadow comparison is a testing technique, not a production provider mode.

### Feature-Specific Contracts

Do not introduce an `IFhirSdkProvider` facade. It would accumulate unrelated serialization, validation, FHIRPath, persistence, and conformance responsibilities.

Each migrated feature uses its existing narrow contract or introduces one narrow contract. Phase 0 uses the existing `IImportResourceParser` contract unchanged.

### Provider Projects

Add four version-specific Firely provider projects:

- `Microsoft.Health.Fhir.Stu3.FirelySdk`
- `Microsoft.Health.Fhir.R4.FirelySdk`
- `Microsoft.Health.Fhir.R4B.FirelySdk`
- `Microsoft.Health.Fhir.R5.FirelySdk`

Each project references its matching version Core project and Firely model package. The four projects should link or import one shared `FirelyImportResourceParser` source file so behavior cannot drift by FHIR version.

Add one multi-version Ignixa project:

- `Microsoft.Health.Fhir.Ignixa`

It references non-version Core contracts and Ignixa's generated schema providers. It selects `STU3CoreSchemaProvider`, `R4CoreSchemaProvider`, `R4BCoreSchemaProvider`, or `R5CoreSchemaProvider` from the existing `IModelInfoProvider.Version`.

Provider assemblies may depend on Core. Core must not depend on provider assemblies. The version-specific API composition root references its matching Firely provider and the shared Ignixa provider.

### .NET Target Framework Boundary

The repository currently targets `net9.0;net8.0`. Ignixa packages used by the prior investigations target net9 only.

Phase 0 must not remove net8:

- Firely provider projects target both net8 and net9.
- `Microsoft.Health.Fhir.Ignixa` targets net9.
- Version API projects conditionally reference Ignixa only for net9.
- The composition root conditionally compiles the Ignixa registration on net9.
- Configuring `FhirSdkProvider=Ignixa` on net8 fails startup with a clear unsupported-provider error.
- Firely remains fully buildable and runnable on both TFMs.

Moving the whole server to net9 is a separate platform decision and must not be hidden in the Ignixa import PR.

## Phase 0: `$import` Provider Seam

Phase 0 is one PR with one behavior change: `$import` may opt into Ignixa parsing on net9.

### Composition

`OperationsModule` reads `CoreFeatureConfiguration.FhirSdkProvider` once during startup and registers exactly one `IImportResourceParser`:

- `Firely` registers `FirelyImportResourceParser`.
- `Ignixa` registers `IgnixaImportResourceParser` on net9.
- `Ignixa` on net8 throws during startup.
- Unknown enum values throw during startup.

Do not resolve both parsers per resource. Do not catch an Ignixa parsing failure and retry with Firely.

### Data Flow

```text
NDJSON line
  -> selected IImportResourceParser
  -> provider-specific parse and mutation
  -> existing ResourceElement compatibility boundary
  -> existing IResourceWrapperFactory
  -> existing ImportResource
  -> unchanged ImportResourceLoader
  -> unchanged SQL/Cosmos import pipeline
```

Both providers must preserve the current import policy:

- Parse a FHIR JSON resource.
- Require a valid FHIR resource ID.
- Reject conditional references during initial load.
- Allow conditional references during incremental load.
- Initialize `meta` when absent.
- Normalize `meta.lastUpdated` to milliseconds.
- Reject a supplied future `lastUpdated`.
- Preserve a valid numeric version when allowed.
- Assign version `1` when version preservation is not allowed.
- Detect soft-deleted resources.
- Remove the Azure soft-delete extension before persistence.
- Create the existing `ResourceWrapper` with the same flags.

No provider-specific resource type may escape the parser boundary.
In Phase 0, the Ignixa provider intentionally adapts its parsed node to the existing
Firely-shaped `ResourceElement` before calling `IResourceWrapperFactory`. Preserving the
native Ignixa node is deferred to the persistence-codec phase; attempting it in Phase 0
would change indexing, raw serialization, and storage behavior.

### Import Semantic Risk

Conditional-reference detection is the highest-risk behavior in the Ignixa parser. The previous prototype inspected metadata for only the root resource and could miss references in contained or embedded resources.

The Ignixa implementation must traverse the full resource graph, including:

- Contained resources.
- Bundle entries.
- Nested backbone elements.
- Choice elements containing references.

It must match the current Firely `GetAllChildren<ResourceReference>()` behavior before selection is enabled.

### Error Handling

- Normalize provider parser failures to the exception categories already handled by the import pipeline.
- Preserve existing externally visible error messages where tests or clients depend on them.
- Reject unsupported runtime/provider combinations during startup, not on the first import line.
- Do not silently skip malformed elements.
- Do not perform success-shaped fallback to Firely.

### Observability

Keep Phase 0 observability small:

- Log the configured provider and migrated seams once at startup.
- Include the selected provider in import parser failure logs using existing logging infrastructure.
- Reuse existing import job success/failure metrics.

New dashboards, metric dimensions, and fallback-guard infrastructure are separate operational changes unless an existing metric can accept the provider dimension without widening the PR materially.

## Phase 0 File-Scope Guardrail

The provider projects and build plumbing create unavoidable new files, but the number of modified production C# files should remain small.

Expected existing production files changed:

- `CoreFeatureConfiguration.cs`
- `OperationsModule.cs`
- The current shared `ImportResourceParser.cs` location, moved or converted to linked Firely provider source

Expected mechanical changes:

- Central package versions.
- Solution entries.
- Four version API project references.
- Four version Core unit-test project references.
- Docker restore project-copy entries if required by the current Dockerfile pattern.

Expected new implementation files:

- `FhirSdkProvider.cs`
- Four Firely project files using one shared parser source.
- One Ignixa project file.
- Ignixa schema context.
- Ignixa import parser.
- Shared provider contract/parity tests.

If Phase 0 requires changes to formatters, persistence registrations, validation, FHIRPath, SQL, Cosmos, or controllers, stop and split the work. Those changes indicate the import boundary is leaking.

## Phase 0 Test Strategy

### Shared Provider Contract Suite

Run the same behavior suite against Firely and Ignixa for every supported FHIR version:

- Valid resource import.
- Missing and invalid IDs.
- IDs at the 64-character boundary.
- Missing `meta`.
- Missing, invalid, and valid numeric versions.
- Millisecond `lastUpdated` normalization.
- Future `lastUpdated` rejection.
- Initial versus incremental import.
- Soft-delete detection and extension removal.
- Top-level conditional references.
- Nested and contained conditional references.
- Conditional references in Bundle entries.
- Malformed primitives and malformed JSON.
- Unknown resource types.

Compare:

- `ImportResource` flags.
- Resource type and ID.
- Normalized raw JSON semantics.
- Version and last-updated values.
- Search-wrapper inputs.
- Exception category and stable message content.

JSON property order is not a parity requirement.

### Build Matrix

- Firely: STU3, R4, R4B, and R5 on net8 and net9.
- Ignixa: STU3, R4, R4B, and R5 on net9.
- Startup rejection: Ignixa configured on net8.
- Existing import unit tests remain green.
- Targeted SQL and Cosmos import tests remain green without storage code changes.

### Rollout

1. Merge with Firely as the default.
2. Deploy with Firely explicitly configured to prove configuration binding.
3. Run the existing import corpus with Ignixa in a non-production environment.
4. Compare persisted wrappers and import outcomes against Firely.
5. Enable Ignixa for a canary import workload.
6. Roll back by setting the provider to Firely; no data migration is required.

## Incremental Migration Ladder

Every later item is a separate PR unless noted. A PR adds only one runtime seam to the global provider setting.

### Phase 1: Export NDJSON Serialization

Switch only the existing `IResourceToByteArraySerializer` used by export.

Exit criteria:

- Byte/semantic parity corpus across all versions.
- Deleted and historical resources covered.
- No HTTP formatter or persistence changes.

### Phase 2: Persistence Codecs

Split into two PRs:

1. Provider-selected `IResourceDeserializer` for stored-resource reads.
2. Provider-selected `IRawResourceFactory` for writes.

Requirements:

- Preserve the native Ignixa node after a database read.
- Continue returning existing Core types to SQL and Cosmos.
- Prove SQL/Cosmos parity for raw JSON, search values, history, and deleted resources.
- Keep rollback possible without rewriting stored data.

### Phase 3: FHIRPath and Search Indexing

Split into three PRs:

1. Introduce a provider-neutral FHIRPath evaluation context with reference resolution and variables.
2. Add a Firely-authoritative parity corpus for every generated search parameter and all FHIR versions.
3. Switch indexing and reindex together after parity is demonstrated.

`resolve()` behavior is a release blocker. Search indexing and reindex must use the same provider to avoid index drift.

### Phase 4: Ordinary HTTP JSON Ingress

Switch create/update parsing for a single resource.

Explicitly exclude:

- Bundle and transaction/batch.
- Parameters resources.
- JSON Patch and FHIRPath Patch.
- XML.

Normalize parser failures to the existing OperationOutcome behavior.

### Phase 5: HTTP JSON Egress

Split into separate PRs:

1. Single-resource responses.
2. Search/history bundles with raw entries.
3. `_summary` and `_elements` projection.

Do not introduce a large custom projection implementation in the first output PR.

### Phase 6: Validation

Split into:

1. Primitive and structural validation.
2. Conformance-resource validation.
3. Profile and terminology-backed validation.

Ignixa success must not synchronously invoke Firely validation. Use characterization tests or offline comparison during migration instead.

### Phase 7: Complex Write Semantics

One operation family per PR:

- Conditional-reference mutation.
- Transaction/batch bundles.
- JSON Patch.
- FHIRPath Patch.
- Bulk update and bulk codecs.

Each PR must cover both direct requests and bundle-contained execution where applicable.

### Phase 8: Remaining Runtime and Tooling Surfaces

Migrate independently:

- CapabilityStatement and conformance construction.
- Terminology.
- Resource parser tools.
- XML.

XML requires an explicit retain, replace, or remove decision. Firely cannot be deleted while supported XML behavior still depends on it.

### Phase 9: Firely Removal

Removal begins only after an inventory reports zero Firely runtime seams for the supported product surface.

Then:

- Delete the four Firely provider projects.
- Remove Firely package references and adapters.
- Remove provider selection code and the enum if Ignixa is the only provider.
- Remove compatibility conversions no longer required by public contracts.
- Run the full version, store, and operation matrix.

## PR Guardrails

Every migration PR must:

- Change one feature seam.
- Preserve Firely as the configuration default until the final cutover decision.
- Have one composition-root decision point.
- Avoid hidden fallback.
- Reuse existing contracts when they are already narrow.
- Add provider parity tests before activation.
- Keep unrelated framework, dependency, CI, SQL, and operational cleanup out of scope.
- State its rollback action.
- State which runtime seams remain Firely-backed.

As a review heuristic, target no more than roughly ten modified production files per feature PR, excluding new provider project scaffolding and mechanical solution/Docker entries. Exceeding that number is not automatically wrong, but it requires explaining why the seam cannot be split further.

## Weakest Link

The global setting can create a false impression that the whole server uses Ignixa. The mitigation is explicit coverage reporting: configuration documentation and startup logs must list the migrated seams. Every PR updates that list.

The second weakest link is semantic drift at conversion boundaries. Provider-specific objects stop at the narrow feature contract, and parity tests compare the resulting Core behavior rather than only proving that parsing succeeds.

## Completion Criteria

The integration is complete when:

- All supported runtime seams use Ignixa.
- All four FHIR versions pass their semantic suites.
- SQL and Cosmos pass the same storage/indexing corpus.
- No Firely fallback is observed in runtime inventory.
- XML has an explicit supported implementation or has been intentionally removed.
- Firely projects, packages, adapters, and mode-selection code are deleted.

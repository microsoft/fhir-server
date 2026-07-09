# Ignixa SDK Next Steps Design

## Context

PR #5467 integrates Ignixa into Microsoft FHIR Server and now has a green CI run on the feature branch. Green CI is not enough to merge this safely to `main`. The branch currently behaves as a hybrid: Ignixa is used in several hot paths, Firely remains active in others, and mode selection is implicit in DI registration and object shape rather than controlled by an explicit runtime contract.

The merge goal is stricter:

1. The server can run in a complete Firely mode across every runtime surface.
2. The server can run in a complete Ignixa mode across every runtime surface.
3. SDK-specific behavior is selected through explicit abstractions and providers.
4. Gaps and blockers are prioritized by severity.
5. Ignixa compatibility shims are minimized and any remaining shim is explicitly deferred with an owner and removal condition.

This design treats every runtime surface as in scope: request parsing, response formatting, create/read/update/delete, PATCH, search, include/revinclude/reference resolution, validation, conformance/terminology behavior, bundles, import/export, bulk update, reindex, async jobs, and test hosting.

## Current State

The branch already has meaningful Ignixa integration:

- Ignixa JSON input and output formatters are registered ahead of Firely formatters.
- JSON request parsing can return `ResourceJsonNode`, `IgnixaResourceElement`, `ResourceElement`, or Firely `Resource`.
- Input formatting no longer JSON round-trips for Firely `Resource`; it converts via `ITypedElement.ToPoco<Resource>()`.
- `RawResourceFactory` uses Ignixa serialization when a `ResourceElement` carries a `ResourceJsonNode`.
- Import parsing uses Ignixa parsing and builds wrappers through the normal wrapper factory.
- NDJSON export uses Ignixa serialization when the resource carries an Ignixa node.
- Search indexing uses the `IFhirPathProvider` abstraction and can use `IgnixaFhirPathProvider`.
- Validation uses `IgnixaResourceValidator` for Ignixa-backed resources and Firely fallback for other cases.

The problem is not that Ignixa is absent. The problem is that the code does not prove complete modes:

- There is no single SDK runtime mode configuration.
- Ignixa registration is largely unconditional in `FhirModule` and `ValidationModule`.
- `SearchModule` has a Firely default provider, while `FhirModule` replaces it with Ignixa without a mode gate.
- `PersistenceModule` registers `IRawResourceFactory` as `RawResourceFactory`, whose behavior depends on whether the resource carries an Ignixa node.
- `ResourceDeserializer` is configured with an Ignixa JSON deserializer delegate for JSON, but this is not controlled by a mode contract.
- `IgnixaResourceValidator` explicitly falls back to Firely validation for conformance resources.
- `IgnixaFhirJsonOutputFormatter` falls back to Firely for `_summary` and `_elements` projection.
- FHIRPath PATCH still depends on Firely model/patch infrastructure.
- Remaining Firely/Ignixa bridges are not tracked as approved deferments.

## Goals

- Add an explicit SDK runtime mode that governs all SDK-specific services.
- Make Firely mode and Ignixa mode separately bootable, testable, and observable.
- Convert implicit provider selection into explicit provider registrations or provider factories.
- Block hidden Firely fallback in Ignixa mode.
- Block hidden Ignixa use in Firely mode.
- Create a ranked backlog of merge-blocking and follow-up stories.
- Track every remaining shim and define when it can be removed.

## Non-Goals

- Per-tenant SDK mode selection. This is useful later but adds complexity that is not required for merging Ignixa to `main`.
- Treating hybrid behavior as the final production contract. Hybrid mode is useful for rollout and diagnosis, but it does not satisfy complete Firely and complete Ignixa mode requirements.
- Broad unrelated refactoring. Provider seams should be introduced only where they are needed for SDK mode correctness.

## SDK Runtime Modes

The server should expose one explicit SDK mode setting, backed by a single mode provider used during startup and testing.

| Mode | Purpose | Required behavior |
|---|---|---|
| `Firely` | Compatibility baseline and emergency rollback | Active production request/data paths use Firely implementations. Ignixa formatters, validators, FHIRPath provider, serializers, and node wrappers are not active providers. |
| `Ignixa` | Target production mode | Active production request/data paths use Ignixa-native providers. Firely fallback is not silent; any remaining fallback must be represented by an approved merge-blocking or deferred story. |
| `Hybrid` | Rollout helper only | Ignixa-first behavior with Firely fallback where needed. This mode is useful for migration and diagnosis, but it is not the merge definition of done. |

The mode should be validated at startup. Invalid combinations should fail fast with actionable errors. Tests should also be able to configure the mode without relying on environment-specific setup.

## Provider Surfaces

The mode contract must govern these surfaces:

| Surface | Firely mode | Ignixa mode |
|---|---|---|
| MVC input formatters | Firely JSON/XML formatters active | Ignixa JSON formatter active for JSON; XML behavior explicitly defined |
| MVC output formatters | Firely JSON/XML formatters active | Ignixa JSON formatter active, including projection support |
| Resource serialization | Firely serializers | Ignixa serializers for JSON resources |
| Resource deserialization | Firely parser/deserializer | Ignixa parser/deserializer for JSON resources |
| Raw resource factory | Firely serialization path | Ignixa serialization path, not incidental node detection |
| Validation | Firely model validation | Ignixa validation for all supported runtime surfaces |
| FHIRPath/search | Firely provider and typed-element converters | Ignixa provider and approved converter seam |
| PATCH | Firely patch path | Ignixa-native patch path or explicit blocker |
| Import/export | Firely codec path | Ignixa codec path for NDJSON and JSON resources |
| Bundles/transactions | Firely-selected providers | Ignixa-selected providers |
| Bulk update/reindex/jobs | Firely-selected providers | Ignixa-selected providers |
| Conformance/terminology | Firely validation and services | Ignixa-capable validation or explicit blocker |

The selection should happen through DI registration and provider factories rather than conditionals spread through handlers.

## Fallback and Shim Policy

Firely and Ignixa interop is allowed only when it is explicit.

In `Ignixa` mode:

- Hidden fallback to Firely is a defect.
- A fallback path must either fail fast, emit a test failure, or be listed in the shim deferment register.
- Compatibility adapters must identify why they exist, what modes allow them, and what work removes them.

In `Firely` mode:

- Hidden use of Ignixa is a defect.
- Firely mode should prove rollback without relying on Ignixa services.

Approved shim metadata should include:

- Shim name and file.
- Runtime surface.
- Mode where it is allowed.
- Reason it exists.
- Severity.
- Owner.
- Removal condition.
- Test proving expected behavior.

## Priority Model

| Priority | Gap | Reason |
|---|---|---|
| P0 | SDK mode configuration and startup validation | Complete modes cannot be proven without one mode contract. |
| P0 | Mode-aware DI and provider selection | Current behavior is hardcoded or incidental. |
| P0 | Firely mode E2E matrix | Rollback must be verifiable. |
| P0 | Ignixa mode E2E matrix | Production readiness must be verifiable. |
| P0 | No hidden fallback in Ignixa mode | Silent fallback invalidates the mode. |
| P0 | PATCH path | FHIRPath PATCH currently depends on Firely infrastructure. |
| P0 | `_summary` and `_elements` projection | Normal read/search output must not require Firely in Ignixa mode. |
| P0 | Conformance validation strategy | Conformance runtime surfaces are in scope and currently fall back. |
| P0 | Import/export/bulk codec alignment | Bulk paths must honor selected mode. |
| P0 | Persistence read/write mode selection | Store/retrieve behavior must not depend on incidental node shape. |
| P1 | Search converter abstraction | Existing `ITypedElement` boundaries may remain temporarily but need an SDK-aware seam. |
| P1 | Fallback telemetry | Mode usage and fallback attempts must be observable. |
| P1 | Performance gates | Ignixa mode should not regress hot paths without an explicit decision. |
| P1 | Operator documentation | Rollout and rollback need a concrete runbook. |
| P2 | Per-tenant mode | Useful later, not needed for merge-to-main. |

## Proposed User Stories

### SDK-1: Define SDK runtime mode configuration

**Priority:** P0

**As a** deployment operator, **I want** a single SDK runtime mode configuration, **so that** Firely and Ignixa behavior is selected intentionally.

**Acceptance criteria:**

- Supported values are `Firely`, `Ignixa`, and `Hybrid`.
- Invalid values fail startup with a clear error.
- The selected mode is exposed through one provider.
- Tests can set the mode without relying on environment-specific configuration.
- Default behavior is documented explicitly.

### SDK-2: Make DI registration mode-aware

**Priority:** P0

**As a** maintainer, **I want** SDK-specific services registered by mode, **so that** Firely and Ignixa modes do not accidentally mix providers.

**Acceptance criteria:**

- `Firely` mode does not activate Ignixa production formatters, validators, FHIRPath provider, JSON serializers, or node-based persistence paths.
- `Ignixa` mode activates Ignixa providers for all supported JSON runtime paths.
- `Hybrid` mode activation is explicit and documented as rollout-only.
- Startup tests verify the active implementation for each provider surface.

### SDK-3: Add mode-specific test hosting

**Priority:** P0

**As a** maintainer, **I want** the test server to boot in Firely and Ignixa modes, **so that** both modes are independently verified.

**Acceptance criteria:**

- Unit and integration tests can run in each mode.
- E2E test configuration can run each mode.
- CI exposes named mode-specific jobs or categories.
- Mode-specific failures are attributable to the selected mode.

### SDK-4: Enforce no hidden fallback in Ignixa mode

**Priority:** P0

**As a** reviewer, **I want** Ignixa mode to reject unapproved Firely fallback, **so that** the mode means what it says.

**Acceptance criteria:**

- Known fallback points are instrumented.
- Unapproved fallback in Ignixa mode fails tests.
- Approved fallback is represented in the shim register.
- Failure messages include the surface and provider that attempted fallback.

### PATH-1: Prove Firely-only runtime path

**Priority:** P0

**As a** release owner, **I want** Firely mode coverage across every runtime surface, **so that** rollback remains safe.

**Acceptance criteria:**

- Create, read, update, delete, search, include/revinclude, PATCH, bundle, import, export, bulk update, reindex, conformance, and async job tests pass in Firely mode.
- Ignixa services are not required to satisfy the tests.
- Firely behavior matches current expected output and validation semantics.

### PATH-2: Prove Ignixa-only runtime path

**Priority:** P0

**As a** release owner, **I want** Ignixa mode coverage across every runtime surface, **so that** the server can run production traffic without Firely fallback.

**Acceptance criteria:**

- The same runtime surface matrix from PATH-1 passes in Ignixa mode.
- Tests detect unapproved Firely fallback.
- JSON fidelity is validated for representative STU3, R4, R4B, and R5 resources.

### PATH-3: Close PATCH dependency

**Priority:** P0

**As a** FHIR API consumer, **I want** PATCH to run in Ignixa mode, **so that** complete Ignixa mode covers update semantics.

**Acceptance criteria:**

- PATCH operations have an Ignixa-native path or a formally approved blocking decision.
- Add, replace, delete, move, copy, and conditional cases are tested.
- Version and `meta.lastUpdated` behavior matches Firely mode.
- Unapproved Firely model conversion is blocked in Ignixa mode.

### PATH-4: Close projection fallback

**Priority:** P0

**As a** search API consumer, **I want** `_summary` and `_elements` to work in Ignixa mode, **so that** normal projected output does not require Firely serialization.

**Acceptance criteria:**

- `_summary` values used by the server are supported in Ignixa mode.
- `_elements` supports included elements, mandatory elements, resource type, id, meta behavior, contained resources, choice types, and extensions.
- Output equivalence is tested against Firely mode.
- Projection does not route through Firely in Ignixa mode.

### PATH-5: Close conformance validation fallback

**Priority:** P0

**As a** conformance maintainer, **I want** conformance resources validated in Ignixa mode, **so that** complete mode coverage includes metadata resources.

**Acceptance criteria:**

- The current fallback list is converted into an explicit implementation plan.
- StructureDefinition, SearchParameter, CapabilityStatement, CodeSystem, ValueSet, ConceptMap, OperationDefinition, and related conformance resources are covered.
- Ignixa mode either validates these resources natively or blocks the operation with an approved product decision.
- Tests cover valid and invalid conformance resources.

### PATH-6: Align import/export/bulk codecs with selected mode

**Priority:** P0

**As an** operations admin, **I want** bulk paths to honor SDK mode, **so that** large-scale operations do not silently use a different SDK.

**Acceptance criteria:**

- Import parsing, NDJSON export, bulk update, and reindex/job serialization use selected providers.
- Roundtrip tests prove no data loss.
- Soft-delete and metadata handling match existing behavior.
- XML behavior is explicitly documented for each mode.

### PATH-7: Make persistence read/write mode explicit

**Priority:** P0

**As a** data layer maintainer, **I want** resource storage and retrieval selected by mode, **so that** persistence behavior is deterministic.

**Acceptance criteria:**

- Raw resource creation is selected by mode rather than incidental `GetIgnixaNode()` availability.
- Resource deserialization is selected by mode.
- Resource wrapper creation preserves version and lastUpdated semantics in both modes.
- Create, upsert, history, search result materialization, and raw read tests cover both modes.

### SHIM-1: Create shim inventory and deferment register

**Priority:** P1

**As a** reviewer, **I want** every compatibility bridge tracked, **so that** shims do not become hidden architecture.

**Acceptance criteria:**

- Each shim has owner, reason, allowed mode, severity, test, and removal condition.
- Unregistered shims fail review or tests.
- The register is updated when a shim is removed or promoted to supported infrastructure.

### SHIM-2: Reduce search converter typed-element dependency

**Priority:** P1

**As a** search maintainer, **I want** search value conversion to have an SDK-aware seam, **so that** Ignixa search indexing does not rely indefinitely on Firely adapters.

**Acceptance criteria:**

- Search converter boundaries are inventoried.
- A provider or adapter pattern is chosen.
- Remaining `ITypedElement` adapter usage is measured and registered.
- Search indexing correctness and performance are tested in both modes.

### OBS-1: Add mode and fallback telemetry

**Priority:** P1

**As an** SRE, **I want** visibility into selected mode and fallback attempts, **so that** production rollout is diagnosable.

**Acceptance criteria:**

- Logs/counters identify SDK mode and active provider for key surfaces.
- Fallback attempts include surface, mode, reason, and outcome.
- Alerts or dashboards can detect unexpected fallback.
- Telemetry avoids logging PHI.

### PERF-1: Add performance gates for hot paths

**Priority:** P1

**As a** maintainer, **I want** Firely and Ignixa benchmarks, **so that** Ignixa mode does not regress critical paths silently.

**Acceptance criteria:**

- Parse, serialize, validate, FHIRPath/search indexing, import, and export benchmarks exist.
- Thresholds are documented.
- Results are attached to PR validation or release notes.

### DOC-1: Add rollout and rollback runbook

**Priority:** P1

**As a** deployment operator, **I want** a runbook for SDK mode rollout, **so that** production changes are reversible.

**Acceptance criteria:**

- Mode selection and defaults are documented.
- Firely rollback procedure is documented.
- Known limitations and deferred shims are listed.
- Troubleshooting references the telemetry from OBS-1.

### TENANT-1: Evaluate per-tenant mode

**Priority:** P2

**As a** product owner, **I want** a decision on per-tenant SDK mode, **so that** mixed rollout can be considered without complicating the merge gate.

**Acceptance criteria:**

- Costs and benefits are documented.
- Global mode remains the default recommendation unless product requirements change.

## Delivery Plan

### Phase 0: Inventory and mode contract

Stories: SDK-1, SHIM-1.

Outcome: The branch has a single vocabulary for SDK mode and a complete list of known compatibility bridges.

### Phase 1: Provider wiring and test harness

Stories: SDK-2, SDK-3, SDK-4, initial PATH-1/PATH-2 scaffolding.

Outcome: Both modes can boot, and tests can assert which providers are active.

### Phase 2: Close P0 runtime blockers

Stories: PATH-3, PATH-4, PATH-5, PATH-6, PATH-7.

Outcome: Complete runtime surface coverage is available for Firely and Ignixa modes.

### Phase 3: Confidence gates

Stories: OBS-1, PERF-1, DOC-1.

Outcome: The mode can be operated, diagnosed, and rolled back.

### Phase 4: Optional expansion

Stories: SHIM-2 deeper optimization, TENANT-1.

Outcome: Remaining adapters are reduced or intentionally supported.

## Test Matrix

Each runtime surface should be tested in both Firely and Ignixa modes.

| Surface | Firely mode | Ignixa mode |
|---|---|---|
| JSON create/read | Required | Required |
| JSON update/delete/history | Required | Required |
| Search and includes | Required | Required |
| FHIRPath indexing | Required | Required |
| PATCH | Required | Required |
| Bundle/transaction | Required | Required |
| Import | Required | Required |
| Export | Required | Required |
| Bulk update | Required | Required |
| Reindex and async jobs | Required | Required |
| Validation | Required | Required |
| Conformance resources | Required | Required |
| Projection | Required | Required |

## Risks

- The weakest link is PATCH and conformance validation. These are the most likely to reveal true SDK capability gaps rather than registration gaps.
- A global mode is reversible. Per-tenant mode is not needed for this merge and would make testing and provider selection harder.
- Hybrid mode is useful but can hide defects. It should not be the definition of done.
- Search may remain adapter-heavy longer than other paths. That is acceptable only if the shim register and telemetry make it visible.

## Approval Criteria for Merge to Main

- Firely mode passes the full runtime surface matrix.
- Ignixa mode passes the full runtime surface matrix.
- Ignixa mode has no unapproved Firely fallback.
- Firely mode has no active dependency on Ignixa production services.
- All P0 stories are complete.
- Remaining P1/P2 shims are registered with removal conditions.
- Operator documentation explains rollout and rollback.

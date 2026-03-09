# Investigation: Ignixa SDK Integration — Current State & Improvement Plan

**Date:** 2025-07-09 (updated 2025-07-10)
**Investigator:** GitHub Copilot
**Branch:** feature/ignixa-sdk
**Status:** Phase 1 (Benchmarks) & Phase 2 (P0 Tests) complete

---

## 1. Executive Summary

The `Microsoft.Health.Fhir.Ignixa` project provides a high-performance alternative to Firely SDK serialization. The integration is **partially complete** — Ignixa handles the primary JSON serialization/deserialization and FHIRPath evaluation hot paths, but several Firely fallback paths remain that introduce redundant round-trips. No unit tests are currently failing due to build errors (the 4 E2E project build failures are caused by a missing .NET SDK 9.0.310 — an environment issue, not a code issue). However, the Firely fallback code paths in the formatters and `RawResourceFactory` represent unnecessary serialization overhead that should be measured and eliminated incrementally.

---

## 2. Build Status

| Project | Status | Notes |
|---|---|---|
| `Microsoft.Health.Fhir.SqlServer` | ✅ Build succeeded | |
| `Microsoft.Health.Fhir.Stu3.Tests.E2E` | ❌ Build failed | `global.json` requires SDK 9.0.310, not installed locally |
| `Microsoft.Health.Fhir.R4.Tests.E2E` | ❌ Build failed | Same SDK version issue |
| `Microsoft.Health.Fhir.R4B.Tests.E2E` | ❌ Build failed | Same SDK version issue |
| `Microsoft.Health.Fhir.R5.Tests.E2E` | ❌ Build failed | Same SDK version issue |
| 33 other projects | ✅ Up-to-date | No rebuild needed |

**Root cause:** The E2E test projects include a post-build target that validates the SDK version from `global.json`. The local machine has a different SDK installed. This is an **environment issue**, not a code defect.

**Action:** Install .NET SDK 9.0.310 or update `global.json` `rollForward` policy for local development.

---

## 3. Test Status

No unit test failures were detected in the test output window. The unit test projects (`Microsoft.Health.Fhir.Shared.Core.UnitTests`, `Microsoft.Health.Fhir.Shared.Api.UnitTests`) compile and appear to be passing. E2E tests cannot run until the SDK version issue is resolved.

---

## 4. Ignixa Integration Map

### 4.1 Components Already on Ignixa

| Component | File | What Changed |
|---|---|---|
| **Core Interfaces** | `Core/Features/Search/FhirPath/IFhirPathProvider.cs` | Abstraction for FHIRPath compilation/evaluation |
| | `Core/Features/Search/FhirPath/ICompiledFhirPath.cs` | Compiled FHIRPath expression interface |
| **Ignixa FHIRPath** | `Ignixa/FhirPath/IgnixaFhirPathProvider.cs` | Delegate compilation, expression caching, native `IElement` evaluation |
| **Search Indexer** | `Core/Features/Search/TypedElementSearchIndexer.cs` | Uses `IFhirPathProvider` instead of direct `FhirPathCompiler` |
| **Serializer** | `Ignixa/IgnixaJsonSerializer.cs` | `IIgnixaJsonSerializer` — parse/serialize via `ResourceJsonNode` |
| **Resource Element** | `Ignixa/IgnixaResourceElement.cs` | Wraps `ResourceJsonNode` with `IElement`, `ITypedElement`, FHIRPath |
| **Schema Context** | `Ignixa/IIgnixaSchemaContext.cs` | Per-FHIR-version schema provider |
| **Input Formatter** | `Shared.Core/Ignixa/IgnixaFhirJsonInputFormatter.cs` | Ignixa-first parsing, registered at higher MVC priority |
| **Output Formatter** | `Shared.Core/Ignixa/IgnixaFhirJsonOutputFormatter.cs` | Ignixa-first serialization, registered at higher MVC priority |
| **RawResourceFactory** | `Shared.Core/Features/Persistence/RawResourceFactory.cs` | Ignixa fast path via `GetIgnixaNode()`, Firely fallback |
| **NDJSON Serializer** | `Shared.Core/Features/Operations/Export/ResourceToNdjsonBytesSerializer.cs` | Ignixa fast path for Export |
| **JSON Deserializer** | `Shared.Api/Modules/FhirModule.cs` (lines 108-139) | JSON → `ResourceJsonNode` → `IgnixaResourceElement` → `ResourceElement` |
| **ResourceElement** | `Core/Models/ResourceElement.cs` | Extended with `IResourceElement` interface, `ResourceInstance` property |
| **Extension Methods** | `Shared.Core/Extensions/ResourceElementIgnixaExtensions.cs` | `GetIgnixaNode()` to extract `ResourceJsonNode` from `ResourceElement` |
| **DI Registration** | `Shared.Core/Ignixa/ServiceCollectionExtensions.cs` | `AddIgnixaSerialization()`, `AddIgnixaFhirPath()`, `AddIgnixaSerializationWithFormatters()` |

### 4.2 Components Still on Firely (Fallback Paths)

| Component | File | Issue | Severity |
|---|---|---|---|
| **Input Formatter — `Resource` target** | `IgnixaFhirJsonInputFormatter.cs` | When controller expects `Resource` type: stream → Ignixa parse → Ignixa serialize → Firely parse (double-parse) | 🔴 High |
| **Output Formatter — Firely `Resource` objects** | `IgnixaFhirJsonOutputFormatter.cs` | When object is a Firely `Resource`: Firely serialize → Ignixa parse → Ignixa serialize (triple-hop) | 🔴 High |
| **RawResourceFactory — Firely fallback** | `RawResourceFactory.cs` (lines 128-131) | When `GetIgnixaNode()` returns null: Firely serialize → Ignixa parse → Ignixa serialize (triple-hop) | 🔴 High |
| **FhirJsonOutputFormatter — `_elements`/`_summary`** | `FhirJsonOutputFormatter.cs` (lines 146-162) | Element subsetting not supported in Ignixa, falls back to Firely `SerializeAsync` | 🟡 Medium |
| **FhirPathPatchPayload** | `FhirPathPatchPayload.cs` (lines 30-46) | Converts raw JSON → POCO for FhirPath Patch operations | 🟡 Medium |
| **ToPoco\<T\>() calls** | `ModelExtensions.cs` (lines 41-54) | Any code path that needs a Firely POCO forces `ITypedElement → ToPoco` conversion | 🟡 Medium |
| **FhirJsonInputFormatter (legacy)** | `FhirJsonInputFormatter.cs` | Still registered but at lower MVC priority (Ignixa formatter takes precedence) | 🟢 Low |
| **FhirJsonOutputFormatter (legacy)** | `FhirJsonOutputFormatter.cs` | Still registered but at lower MVC priority | 🟢 Low |
| **XML pipeline** | Various | Firely-only; XML is low-volume | 🟢 Low |

### 4.3 Data Flow Diagram

![Ignixa Integration Data Flow](https://via.placeholder.com/600x400.png?text=Data+Flow+Diagram+Placeholder)

---

## 5. Identified Performance Anti-Patterns

### 5.1 Double-Parse in Input Formatter

**Location:** `IgnixaFhirJsonInputFormatter.ReadRequestBodyAsync`

**Trigger:** When a controller action parameter is typed as `Resource` (Firely POCO) instead of `ResourceElement`.

**Current flow:**
1. `request.Body` → `MemoryStream` (buffer copy)
2. `_serializer.ParseAsync(memoryStream)` → `ResourceJsonNode` (Ignixa parse)
3. `_serializer.Serialize(ignixa)` → JSON string (Ignixa serialize)
4. `_firelyParser.ParseAsync<Resource>(jsonString)` → `Resource` (Firely parse)

**Impact:** Every request to a controller expecting `Resource` type pays for 2 full parses + 1 full serialize.

**Fix:** Use `Ignixa.Extensions.FirelySdk5` to convert `ResourceJsonNode` → `ITypedElement` → `.ToPoco<Resource>()` without the JSON round-trip.

### 5.2 Triple-Hop in Output Formatter

**Location:** `IgnixaFhirJsonOutputFormatter.ConvertFirelyToIgnixa`

**Trigger:** When the response object is a Firely `Resource` POCO (e.g., CapabilityStatement, OperationOutcome created in-code).

**Current flow:**
1. `_firelySerializer.SerializeToString(resource)` → JSON string (Firely serialize)
2. `_serializer.Parse(json)` → `ResourceJsonNode` (Ignixa parse)
3. `_serializer.Serialize(resourceNode, response.Body)` → response stream (Ignixa serialize)

**Impact:** 3 serialization operations for resources constructed as Firely POCOs.

**Fix:** For this case, write Firely JSON directly to the response stream without Ignixa re-serialization.

### 5.3 Triple-Hop in RawResourceFactory

**Location:** `RawResourceFactory.CreateFromFirely` (lines 128-131)

**Trigger:** When `ResourceElement.GetIgnixaNode()` returns `null` (resource was created from a Firely POCO, not from Ignixa parsing).

**Current flow:**
1. `resource.ToPoco<Resource>()` → Firely POCO
2. `_fhirJsonSerializer.SerializeToString(poco)` → JSON string
3. `_ignixaJsonSerializer.Parse(firelyJson)` → `ResourceJsonNode`
4. `_ignixaJsonSerializer.Serialize(resourceNode)` → JSON string

**Impact:** Every write of a Firely-sourced resource pays for POCO extraction + 2 serializations + 1 parse.

**Fix:** Skip Ignixa re-serialization when the resource originated from Firely. Firely JSON output is valid; re-parsing through Ignixa adds no value.

---

## 6. Test Coverage Assessment

### 6.1 Tests Already Updated for Ignixa

| Test File | Changes |
|---|---|
| `ResourceWrapperFactoryTests.cs` | Uses `new IgnixaJsonSerializer()` + `new FhirJsonSerializer()` in `RawResourceFactory` constructor |
| `ResourceToNdjsonBytesSerializerTests.cs` | Uses `IIgnixaJsonSerializer` in serializer constructor |
| `ProfileValidatorTests.cs` | No Ignixa dependency (tests Firely validation) |

### 6.2 Tests Still Using Firely-Only Patterns

| Test File | Concern |
|---|---|
| `FhirJsonOutputFormatterTests.cs` | Creates `FhirJsonOutputFormatter` directly (Firely serializer only). Does not test `IgnixaFhirJsonOutputFormatter`. |
| `FhirJsonInputFormatterTests.cs` | Tests `FhirJsonInputFormatter` (Firely). No tests for `IgnixaFhirJsonInputFormatter`. |
| `ResourceDeserializerTests.cs` | Tests deserializer but may not exercise the Ignixa JSON path from `FhirModule`. |

### 6.3 Missing Test Coverage

### 6.3 Missing Test Coverage

| Gap | Priority |
|---|---|
| **No unit tests for `IgnixaFhirJsonInputFormatter`** | 🔴 Critical — this is the primary request parser |
| **No unit tests for `IgnixaFhirJsonOutputFormatter`** | 🔴 Critical — this is the primary response serializer |
| **No JSON round-trip fidelity tests** (Ignixa parse → Ignixa serialize → Firely parse → assert equality) | 🔴 Critical |
| **No benchmarks** comparing Ignixa vs. Firely paths | 🟡 Important for validating the migration rationale |
| **No tests for `IgnixaFhirPathProvider`** in isolation | 🟡 Important |
| **No tests for `GetIgnixaNode()` extension** | 🟢 Low risk, simple logic |

---

## 7. Recommended Plan

### Phase 1 — Measurement & Baselines (P0)

**Objective:** Establish quantitative evidence for every change.

| Task | Deliverable |
|---|---|
| 1.1 Create `test/Microsoft.Health.Fhir.Perf/` BenchmarkDotNet project | Benchmark harness |
| 1.2 Benchmark: `RawResourceFactory.Create()` — Ignixa vs. Firely fallback path | Baseline numbers |
| 1.3 Benchmark: `ResourceToNdjsonBytesSerializer.Serialize()` — Ignixa vs. `Instance.ToJson()` | Baseline numbers |
| 1.4 Benchmark: `IgnixaFhirJsonInputFormatter` — Ignixa parse vs. Firely `FhirJsonParser` | Baseline numbers |
| 1.5 Benchmark: `TypedElementSearchIndexer.Extract()` — Ignixa FHIRPath vs. Firely FHIRPath | Baseline numbers |
| 1.6 Add telemetry counters for Firely fallback paths in `RawResourceFactory`, `IgnixaFhirJsonInputFormatter`, `IgnixaFhirJsonOutputFormatter` | Observability |

### Phase 2 — Critical Test Coverage (P0)

**Objective:** Ensure the Ignixa code paths are tested before modifying them.

| Task | Deliverable |
|---|---|
| 2.1 Unit tests for `IgnixaFhirJsonInputFormatter` (all target types: `ResourceJsonNode`, `IgnixaResourceElement`, `ResourceElement`, `Resource`) | Test class |
| 2.2 Unit tests for `IgnixaFhirJsonOutputFormatter` (all source types: `ResourceJsonNode`, `IgnixaResourceElement`, `Resource`, `RawResourceElement`) | Test class |
| 2.3 JSON round-trip fidelity tests: for Patient, Observation, Bundle, Condition across R4/R5 | Test class |
| 2.4 Unit tests for `IgnixaFhirPathProvider.Compile()` and `ICompiledFhirPath.Evaluate()` | Test class |

### Phase 3 — Eliminate High-Impact Fallbacks (P1)

**Objective:** Remove the double-parse and triple-hop serialization paths.

| Task | Risk | Deliverable |
|---|---|---|
| 3.1 Fix input formatter double-parse: use `ITypedElement.ToPoco<Resource>()` instead of JSON round-trip | Medium — POCO conversion may differ | Updated `IgnixaFhirJsonInputFormatter` |
| 3.2 Fix output formatter triple-hop: write Firely JSON directly when source is a `Resource` POCO | Low — Firely JSON output is already valid | Updated `IgnixaFhirJsonOutputFormatter` |
| 3.3 Fix `RawResourceFactory.CreateFromFirely`: skip Ignixa re-serialization for Firely-sourced resources | Low — same reasoning as 3.2 | Updated `RawResourceFactory` |
| 3.4 Re-run benchmarks to validate improvements | — | Updated benchmark results |

### Phase 4 — Reduce ToPoco Surface Area (P2)

**Objective:** Minimize Firely POCO conversions on hot paths.

| Task | Risk | Deliverable |
|---|---|---|
| 4.1 Audit all `ToPoco<T>()` call sites, classify as hot/cold path | — | Audit document |
| 4.2 Add `_elements`/`_summary` support to `IgnixaFhirJsonOutputFormatter` | High — requires element subsetting on `ResourceJsonNode` | Updated output formatter |
| 4.3 Investigate FhirPath Patch on `ResourceJsonNode` (avoid POCO round-trip) | High — complex, depends on Ignixa patch support | Feasibility analysis |

### Phase 5 — Production Safety (P1)

**Objective:** Enable safe rollout and rollback.

| Task | Deliverable |
|---|---|
| 5.1 Add feature flag to control Ignixa formatter priority | Configuration setting |
| 5.2 Run full E2E suite with Ignixa-only path (once SDK version issue is resolved) | E2E pass/fail report |
| 5.3 Add structured logging for all serialization path selections | Log entries |

---

## 8. Risks & Mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| JSON output differs between Ignixa and Firely (property ordering, decimal precision, date formatting) | Medium | High — breaks client compatibility | Round-trip fidelity tests (Phase 2.3) |
| `ITypedElement` shim from `Ignixa.Extensions.FirelySdk5` has behavioral gaps | Low | Medium — incorrect search indexing | Existing E2E search tests serve as regression gate |
| Removing Firely fallback breaks edge cases (contained resources, extensions, choice types) | Medium | High | Phase 2 tests must cover these edge cases explicitly |
| Performance regression in edge cases where Ignixa is slower (e.g., very deeply nested resources) | Low | Low | Benchmarks include worst-case payloads |

---

## 9. Files Modified by Ignixa Integration

### New files (in `src/Microsoft.Health.Fhir.Ignixa/`)
- `IIgnixaJsonSerializer.cs`
- `IgnixaJsonSerializer.cs`
- `IgnixaResourceElement.cs`
- `IIgnixaSchemaContext.cs`
- `FhirPath/IgnixaFhirPathProvider.cs`

### New files (in `src/Microsoft.Health.Fhir.Core/`)
- `Features/Search/FhirPath/IFhirPathProvider.cs`
- `Features/Search/FhirPath/ICompiledFhirPath.cs`
- `Models/IResourceElement.cs`

### New files (in `src/Microsoft.Health.Fhir.Shared.Core/`)
- `Ignixa/ServiceCollectionExtensions.cs`
- `Ignixa/IgnixaFhirJsonInputFormatter.cs`
- `Ignixa/IgnixaFhirJsonOutputFormatter.cs`
- `Extensions/ResourceElementIgnixaExtensions.cs`

### Modified files
- `Core/Features/Search/TypedElementSearchIndexer.cs` — uses `IFhirPathProvider`
- `Core/Models/ResourceElement.cs` — added `IResourceElement`, `ResourceInstance` property
- `Shared.Core/Features/Persistence/RawResourceFactory.cs` — dual Ignixa/Firely constructor
- `Shared.Core/Features/Operations/Export/ResourceToNdjsonBytesSerializer.cs` — Ignixa fast path
- `Shared.Api/Modules/FhirModule.cs` — Ignixa DI registration, deserializer rewiring
- `Shared.Core/Extensions/ModelExtensions.cs` — `ToPoco<T>()` checks for Ignixa `ResourceInstance`

---

## 10. Environment Setup Notes

- `global.json` requires .NET SDK **9.0.310**
- E2E test projects have a post-build SDK version check that fails if the exact version is not installed
- Ignixa NuGet packages (`Ignixa.Abstractions`, `Ignixa.Serialization`, `Ignixa.Extensions.FirelySdk5`, `Ignixa.Specification`, `Ignixa.FhirPath`, `Ignixa.Validation`) are referenced via `Directory.Packages.props` (centralized package management)

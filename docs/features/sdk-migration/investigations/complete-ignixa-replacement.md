# Investigation: Complete Firely SDK Replacement with Ignixa

**Feature**: sdk-migration
**Status**: In Progress
**Created**: 2025-12-30

## Approach

Replace the Firely SDK entirely with Ignixa SDK packages, adopting Ignixa's unified architecture where a single set of assemblies handles all FHIR versions (STU3, R4, R4B, R5, R6) through:

1. **Version-agnostic abstractions** - `IType`, `ISchema`, `FhirVersion` enum
2. **Pre-generated schema providers** - One per FHIR version, all in single assembly
3. **Runtime version routing** - `FhirVersionContext` dispatches to correct provider
4. **HTTP header version negotiation** - `fhirVersion=4.0` in Content-Type/Accept

### Ignixa Packages to Use

| Package | Purpose | Replaces |
|---------|---------|----------|
| `Ignixa.Abstractions` | Core interfaces (ResourceKey, IType, ISchema) | Hl7.Fhir.ElementModel interfaces |
| `Ignixa.Serialization` | High-perf JSON serialization | FhirJsonSerializer/Parser |
| `Ignixa.Specification` | Pre-generated schema providers | Hl7.Fhir.Specification.* |
| `Ignixa.Search` | Search indexing & parameters | TypedElementSearchIndexer |
| `Ignixa.FhirPath` | FhirPath evaluation | Hl7.FhirPath |
| `Ignixa.Validation` | Profile validation | Hl7.Fhir.Validation.* |

## Tradeoffs

| Pros | Cons |
|------|------|
| Single assembly for all FHIR versions | Large migration effort (400+ files) |
| No conditional compilation needed | Ignixa is newer, less battle-tested |
| HTTP header version negotiation (FHIR compliant) | Learning curve for team |
| Pre-generated schema = faster startup | May need custom extensions for edge cases |
| Mutable JSON nodes enable efficient FHIR Patch | Terminology operations need verification |
| Zero-copy serialization potential | Firely-specific workarounds may not translate |
| Multi-tenant package support built-in | E2E tests still need Firely (out of scope) |
| Future R6 support already available | Community/support ecosystem smaller |

## Alignment

- [x] Follows layer rules (API -> App -> Domain -> Data)
- [x] F5 Developer Experience (works with minimal setup)
- [x] FHIR spec compliance (if applicable)
- [ ] Consistent with existing patterns (requires significant pattern changes)

## Evidence

### Current Firely SDK Integration Points

Based on codebase analysis, Firely SDK is used in these critical areas:

#### 1. Serialization (CRITICAL - 306+ files)

**Current:**
```csharp
// FhirModule.cs
var jsonParser = new FhirJsonParser(new ParserSettings() {
    PermissiveParsing = true,
    TruncateDateTimeToDate = true
});
var jsonSerializer = new FhirJsonSerializer();
services.AddSingleton(jsonParser);
services.AddSingleton(jsonSerializer);
```

**Ignixa Equivalent:**
```csharp
// Uses System.Text.Json with ResourceJsonNode
// No parser settings - uses ISchema for type metadata
var resource = ResourceJsonNode.Parse(jsonBytes, schema);
var bytes = resource.ToUtf8Bytes();
```

#### 2. Model Types (CRITICAL - 471+ files)

**Current:** Version-specific types like `Hl7.Fhir.R4.Model.Patient`

**Ignixa Equivalent:** Generic `ResourceJsonNode` with schema-driven access:
```csharp
var patient = new ResourceJsonNode("Patient", schema);
patient["name"] = new JsonArray { ... };
```

#### 3. FhirPath Evaluation (HIGH - 56+ files)

**Current:**
```csharp
var compiler = new FhirPathCompiler();
var expression = compiler.Parse(fhirPath);
var results = expression.Evaluate(typedElement);
```

**Ignixa Equivalent:**
```csharp
// Ignixa.FhirPath package
var evaluator = new FhirPathEvaluator(schema);
var results = evaluator.Evaluate(element, fhirPath);
```

#### 4. Validation (HIGH - 89+ files)

**Current:**
```csharp
var resolver = new MultiResolver(
    new CachedResolver(ZipSource.CreateValidationSource(), cacheDuration),
    profilesResolver
);
var validator = new Validator(resolver, ...);
```

**Ignixa Equivalent:**
```csharp
// Ignixa.Validation package
var validator = new ResourceValidator(schemaProvider, profileProvider);
var outcome = validator.Validate(resource);
```

#### 5. Search Indexing (HIGH - 60+ files)

**Current:** `TypedElementSearchIndexer` uses Firely's `ITypedElement`

**Ignixa Equivalent:** `ISearchIndexer` interface with version-specific implementations:
```csharp
var indexer = fhirVersionContext.GetSearchIndexer(fhirVersion);
var entries = indexer.Extract(element);
```

#### 6. Version-Specific Architecture

**Current:** 16 separate projects with conditional compilation:
```
Microsoft.Health.Fhir.Stu3.Core  -> Hl7.Fhir.STU3
Microsoft.Health.Fhir.R4.Core   -> Hl7.Fhir.R4
Microsoft.Health.Fhir.R4B.Core  -> Hl7.Fhir.R4B
Microsoft.Health.Fhir.R5.Core   -> Hl7.Fhir.R5
```

**Ignixa Architecture:** Single assembly with runtime routing:
```csharp
public IFhirSchemaProvider GetBaseSchemaProvider(FhirVersion fhirVersion)
{
    return fhirVersion switch
    {
        FhirVersion.Stu3 => new STU3CoreSchemaProvider(),
        FhirVersion.R4 => new R4CoreSchemaProvider(),
        FhirVersion.R4B => new R4BCoreSchemaProvider(),
        FhirVersion.R5 => new R5CoreSchemaProvider(),
        FhirVersion.R6 => new R6CoreSchemaProvider(),
        _ => throw new ArgumentException($"Unsupported: {fhirVersion}")
    };
}
```

### Ignixa Architecture Key Components

| Component | File | Purpose |
|-----------|------|---------|
| `FhirVersion` | Ignixa.Abstractions | Byte enum for fast version checks |
| `ResourceKey` | Ignixa.Abstractions | Universal resource identifier |
| `IType` / `TypeInfo` | Ignixa.Abstractions | Version-agnostic type metadata |
| `ISchema` / `IFhirSchemaProvider` | Ignixa.Abstractions | Schema abstraction per version |
| `ResourceJsonNode` | Ignixa.Serialization | Mutable JSON wrapper |
| `FhirVersionContext` | Ignixa.Application | Runtime version routing with caching |
| `*CoreSchemaProvider` | Ignixa.Specification | Pre-generated per-version providers |

### Migration Complexity Assessment

| Component | Files Affected | Complexity | Notes |
|-----------|---------------|------------|-------|
| Serialization | 30+ | HIGH | Core I/O, must be first |
| Model types | 400+ | CRITICAL | Pervasive throughout |
| FhirPath | 56+ | MEDIUM | Well-isolated |
| Validation | 89+ | MEDIUM | Can be phased |
| Search indexing | 60+ | MEDIUM | After serialization |
| Conformance | 20+ | LOW | CapabilityStatement |
| Terminology | 10+ | LOW | FirelyTerminologyServiceProxy |

### Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Large migration scope | Phase by component, not by version |
| Breaking changes during migration | Feature flag for gradual rollout |
| Firely-specific workarounds | Document and test edge cases |
| Performance regression | Benchmark before/after |
| E2E test compatibility | Keep Firely in test projects |

## Alternative Approaches

### 1. Partial Migration (Serialization Only)
Replace only serialization layer with Ignixa, keep Firely for models/validation.
- **Pro:** Lower risk, faster delivery
- **Con:** Still have version-specific assemblies

### 2. Abstraction Layer
Create internal abstraction over both Firely and Ignixa, migrate incrementally.
- **Pro:** Gradual migration possible
- **Con:** Maintenance overhead during transition

### 3. Hybrid Approach
Use Ignixa for new features, maintain Firely for existing code.
- **Pro:** No existing code changes
- **Con:** Two SDKs to maintain indefinitely

## Verdict

**Pending further analysis** - The migration is technically feasible but represents a significant undertaking:

### Feasibility: YES with caveats

1. **Ignixa provides equivalents** for all major Firely features used
2. **Architecture is cleaner** - single assembly vs version-specific projects
3. **Performance should improve** - pre-generated schemas, System.Text.Json

### Recommended Approach

If proceeding, recommend a **phased migration**:

1. **Phase 1**: Add Ignixa packages alongside Firely
2. **Phase 2**: Migrate serialization layer (highest impact)
3. **Phase 3**: Migrate search indexing
4. **Phase 4**: Migrate validation
5. **Phase 5**: Migrate FhirPath evaluation
6. **Phase 6**: Remove Firely dependencies, consolidate projects

### Estimated Effort

- **Phase 1-2**: 2-4 weeks (serialization is foundational)
- **Phase 3-5**: 4-6 weeks (can parallelize)
- **Phase 6**: 2-3 weeks (cleanup, testing)
- **Total**: 8-13 weeks for core migration

### Decision Needed

1. Is the unified assembly architecture worth the migration effort?
2. Are there budget/timeline constraints?
3. Should we prototype Phase 1-2 first to validate approach?

## Prototype Status

A working prototype has been created at `src/Microsoft.Health.Fhir.Ignixa/`:

### Files Created

| File | Purpose |
|------|---------|
| `Microsoft.Health.Fhir.Ignixa.csproj` | Project file with Ignixa NuGet packages (v0.0.127) |
| `IResourceElement.cs` | Common interface for resource wrappers |
| `IgnixaResourceElement.cs` | Wraps ResourceJsonNode with schema awareness + Firely shim |
| `IIgnixaJsonSerializer.cs` | Serialization contract interface |
| `IgnixaJsonSerializer.cs` | High-perf JSON serialization using Ignixa |
| `IgnixaFhirJsonInputFormatter.cs` | ASP.NET Core input formatter |
| `IgnixaFhirJsonOutputFormatter.cs` | ASP.NET Core output formatter |
| `ServiceCollectionExtensions.cs` | DI registration helpers |

### Key Findings from Prototype

1. **Ignixa requires Firely SDK 5.13.1+** - The `Ignixa.Extensions.FirelySdk5` package needs a newer Firely version than the main server uses (5.11.4). This creates version conflicts when integrating.

2. **Ignixa packages are net9.0 only** - The packages don't support net8.0, requiring the prototype to be net9.0-only while the main server targets both.

3. **Firely shim works** - The `ToTypedElement()` extension method successfully bridges Ignixa's IElement to Firely's ITypedElement for FhirPath compatibility.

4. **Build succeeds independently** - The prototype builds successfully as a standalone project.

### Integration Challenges

- **Version conflicts**: Need to upgrade Firely SDK from 5.11.4 to 5.13.1+ to use Ignixa shims
- **Multi-targeting**: Main server uses `net9.0;net8.0` but Ignixa is net9.0-only
- **Gradual migration**: Can't simply swap formatters - need to update all resource handling code

## Next Steps

1. [x] Prototype serialization replacement in isolated branch
2. [ ] Upgrade Firely SDK to 5.13.1+ across the solution
3. [ ] Drop net8.0 support (or keep separate code paths)
4. [ ] Benchmark Ignixa vs Firely serialization performance
5. [ ] Identify Firely-specific edge cases that need special handling
6. [ ] Create detailed migration plan per component
7. [ ] Get stakeholder approval for phased approach

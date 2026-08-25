# ADR 2608: Incremental Ignixa SDK Migration — Phase 3 (FHIRPath evaluation)

## Context

[ADR 2607](adr-2607-ignixa-import-phase0.md) established an incremental migration from the Firely SDK to Ignixa, one feature seam per PR, behind a provider setting that defaults to Firely. Phase 0 migrated `$import` parsing. This ADR covers Phase 3, FHIRPath evaluation — the seam the migration's performance rationale ultimately rests on, since FHIRPath is what extracts every search index entry on every write.

Today FHIRPath reaches the server through two unrelated doors:

- **The extension methods.** `Hl7.FhirPath.IValueProviderFPExtensions` adds `Select`/`Scalar`/`Predicate`/`IsTrue`/`IsBoolean` to `ITypedElement`. Roughly 27 production files call these — the twenty search-value converters, the bundle wrappers, `CompartmentDefinitionManager`, `SearchParameterDefinitionBuilder`, `NarrativeValidator`, and others. A file opts in merely by writing `using Hl7.FhirPath;`, so there is no seam to intercept and no way to tell from a call site which engine will run.
- **Direct compilation.** `TypedElementSearchIndexer` holds its own `FhirPathCompiler` and expression cache and invokes the compiled delegate directly.

A third group — `SearchParameterToTypeResolver`, `SearchParameterSupportResolver`, `SearchParameterComparer` — consumes Firely's `Hl7.FhirPath.Expressions` AST for type inference. These never evaluate against a resource.

There is no abstraction over any of this. Replacing the engine therefore means either touching every call site or introducing a seam that the call sites can be moved onto mechanically. Prior full-cutover prototypes (`feature/ignixa-sdk`, `personal/bkowitz/ignixa-sdk-next-steps-fable`) changed 144 files and were not reviewable.

## Decision

Introduce one narrow FHIRPath seam in Core, move every evaluating call site onto it, and select the implementation from configuration at a single composition point.

### The seam

```csharp
public interface IFhirPathProvider { ICompiledFhirPath Compile(string expression); }

public interface ICompiledFhirPath
{
    string Expression { get; }
    IEnumerable<ITypedElement> Select(ITypedElement input, EvaluationContext context = null);
}
```

`Select` is the only primitive. `Scalar`, `Predicate`, `IsTrue`, and `IsBoolean` are derived once, in the seam's extension class, from `Select`. This is deliberate: the existing Ignixa prototype's `Predicate` returned `false` for an empty result where Firely returns `true`, and `ConformanceProviderBase` gates every capability query on `Predicate`. Deriving once removes that class of drift entirely rather than asking two implementations to agree.

The derivation must reproduce Firely 5.11.4 exactly, and its semantics are subtler than they look:

- `Predicate` is `BooleanEval`, not "empty or truthy": empty yields `true`; a single element whose `Value` is a `bool` yields that bool (so `active = false` yields **false**); any other non-empty content yields `true`. `BooleanEval` is `internal` in the SDK, so the seam reimplements it.
- `Scalar` takes two results and calls `Single()`, so **two or more results throw `InvalidOperationException`**. Firely SDK 6 changed this to return null; we are on 5.11.4 and pin the throw.
- Every extension method applies `ToScopedNode()` to its input before evaluating. That wrap is what makes `%resource` and `%rootResource` resolve, and it is part of the observable contract.
- Firely's `Closure.Root` mutates the caller's `EvaluationContext`. The concrete `FhirEvaluationContext` must flow through the seam untouched, or `ElementResolver` — and with it `resolve()` — is silently dropped.

These are pinned by characterization tests written against the Firely provider before any Ignixa code exists.

`FirelyFhirPathProvider` and `FirelyCompiledFhirPath` live in Core beside the interfaces. Core already references `Hl7.Fhir.Base`, which contains the engine, `EvaluationContext`, and `AddFhirExtensions`, so this adds no dependency and avoids touching the four version-specific `*.FirelySdk` projects that Phase 0 created. Final cutover deletes two files.

**The seam owns evaluation symbol-table registration.** `FhirModule` previously called `FhirPathCompiler.DefaultSymbolTable.AddFhirExtensions()` in two places; that global mutation is what puts `resolve()` into the engine. Leaving evaluation registration there means a provider constructed outside full server startup — exactly what the characterization tests do — cannot compile `resolve()` expressions. `FirelyFhirPathProvider` therefore performs the guarded, idempotent registration itself, and `FhirModule` drops both calls. Separately, `SearchModule` unconditionally performs the same guarded registration at the composition root, including in Ignixa mode, because FHIRPath Patch remains Firely-backed until Phase 7.

### Provider selection

The provider is a process-wide ambient, following the pattern `ModelInfoProvider` already establishes in this codebase, because the call sites include static extension methods (`SoftDeletedFhirPathExtension`, the converter helpers, `SearchParameterInfo`) that have no access to DI.

```csharp
public static class FhirPathProvider
{
    private static Func<IFhirPathProvider> _factory = static () => new FirelyFhirPathProvider();
    private static Lazy<IFhirPathProvider> _instance = new(() => _factory());

    public static IFhirPathProvider Instance => _instance.Value;

    public static void SetProviderFactory(Func<IFhirPathProvider> factory)
    {
        _factory = EnsureArg.IsNotNull(factory, nameof(factory));
        _instance = new Lazy<IFhirPathProvider>(() => _factory());
    }
}
```

Two properties matter. The default is Firely, so **nothing has to call the setter for current behaviour to hold** — roughly a thousand unit tests that construct converters directly need no fixture change, unlike `ModelInfoProvider`, which throws when unset. And resolution is lazy behind a `Lazy<T>`, so the provider is built after `ModelInfoProvider` is set and two threads cannot race into two expression caches. `SetProviderFactory` replaces the `Lazy`, so a pre-registration read cannot latch Firely permanently.

`SearchModule` is the single composition point — it already takes `FhirServerConfiguration` and owns the feature area, where `FhirModule` is parameterless:

```csharp
// FHIRPath Patch remains Firely-backed even when evaluation uses Ignixa.
ElementNavFhirExtensions.PrepareFhirSymbolTableFunctions();

FhirPathProvider.SetProviderFactory(
    _configuration.CoreFeatures.FhirSdkProvider.EffectiveFhirPath == FhirSdkProvider.Ignixa
        ? () => new IgnixaFhirPathProvider(new IgnixaSchemaContext(ModelInfoProvider.Instance))
        : () => new FirelyFhirPathProvider());

services.AddSingleton<IFhirPathProvider>(_ => FhirPathProvider.Instance);
```

The DI registration delegates to the ambient rather than constructing a second provider, so there is exactly one expression cache per process. `TypedElementSearchIndexer` takes `IFhirPathProvider` through its constructor rather than reaching for the ambient; it is DI-constructed, and injecting it is what allows the parity corpus to drive both providers in one test process.

### Configuration

Phase 0's scalar `CoreFeatureConfiguration.FhirSdkProvider` becomes a nested section so seams can be rolled out independently:

```csharp
public class FhirSdkProviderConfiguration
{
    public FhirSdkProvider Default { get; set; } = FhirSdkProvider.Firely;
    public FhirSdkProvider? Import { get; set; }
    public FhirSdkProvider? FhirPath { get; set; }

    public FhirSdkProvider EffectiveImport => Import ?? Default;
    public FhirSdkProvider EffectiveFhirPath => FhirPath ?? Default;
}
```

```json
"FhirSdkProvider": { "Default": "Firely", "FhirPath": "Ignixa" }
```

FHIRPath changes search index *content*, where import parsing does not, so the two must be flippable separately. `FhirSdkProviderStartupLogger` logs every effective per-seam value rather than one enum.

### Scope

**In:** the ~27 extension-method call sites, `TypedElementSearchIndexer`, `ResourceElement`, `ConformanceProviderBase`, and reindex (which shares the same `ISearchIndexer` singleton, so it cannot diverge from indexing within a process).

**Out, and permanently marked so:**

- **FHIRPath Patch node selection.** The six `Operation*.cs` files select nodes and then mutate the returned nodes: `ElementModelExtensions.ToElementNode` is `(element is ElementNode el) ? el : ElementNode.FromElement(element)`, and `OperationDelete` then calls `Target.Parent.Remove(Target)`. Firely returns the input tree's own `ElementNode` instances, so the mutation lands. An Ignixa provider returns adapter-wrapped nodes, fails the type test, receives a **detached copy**, and patches the copy — the operation reports success and the resource is unchanged. Ignixa structurally cannot honour node identity across the adapter boundary. Patch is Phase 7 in ADR 2607 regardless; these operation files keep `Hl7.FhirPath` and are whitelisted in the seam test with this reason. `PatchPayload` only compares immutable scalar values, so it uses the provider seam.
- **The three AST consumers.** They perform type inference at definition time, never evaluate against a resource, and porting them to Ignixa's AST plus `FhirPathAnalyzer` is a large visitor rewrite with no runtime benefit. They keep `Hl7.FhirPath` for `FhirPathCompiler` and are whitelisted.
- **`ITypedElement` and `EvaluationContext` stay in the seam signatures.** Both ship in `Hl7.Fhir.Base`, which Core keeps for the element model regardless, so abstracting `EvaluationContext` alone removes no dependency while adding churn. They get replaced together when the element model moves.

### Locking the seam

A test asserts that no file imports `Hl7.FhirPath` outside the Firely provider and the documented whitelist. It must also ban `Hl7.Fhir.FhirPath` — the POCO-based `Select`/`Scalar` extensions live there with their own always-FHIR-enabled cache, so a call site on a POCO would neither collide with the seam nor be caught. It must not prefix-match `Hl7.FhirPath.Sprache`, which is Firely's embedded parser-combinator library and unrelated to the engine (`SqlServerFhirDataStore` and `StringExtensions` use it legitimately). The repo has no `BannedApiAnalyzers` package; a test is cheaper than adding one.

### Cache policy

Stated explicitly because three different policies exist today and none is written down: the extension path uses a shared static 500-entry LRU, `TypedElementSearchIndexer` uses a private unbounded dictionary, and Ignixa keeps its own static unbounded AST and delegate caches. Expressions are influenced by user input through custom search parameters, so unbounded caching is a slow leak.

Each provider owns a **bounded** compile cache sized to hold the generated corpus without thrashing (R4 alone ships roughly 1,400 search-parameter expressions). `TypedElementSearchIndexer` does not retain a second raw-expression cache: each extraction obtains an `ICompiledFhirPath` handle through the selected provider, whose bounded cache is the single cache-policy authority.

### Failure handling

`TypedElementSearchIndexer` catches all exceptions from expression evaluation, logs a warning, and yields an empty index entry set. Ignixa throws `NotSupportedException` for unimplemented functions where Firely would return empty, so that catch is the exact mechanism by which a conformance gap becomes silent index drift. In Ignixa mode, evaluation failure is surfaced as a metric and is a bake-in gate, not a swallowed warning.

## Status

Proposed

## Consequences

### Benefits

- One seam, one composition point, one engine per process. There is no mode in which two FHIRPath implementations run within a single indexing pass.
- The change is dominated by mechanical edits: ~27 files change only their `using` line, bodies untouched. Keeping both namespace imports is a `CS0121` ambiguity, so the compiler proves no call site was missed.
- Firely stays the default and fully functional; rollback is a configuration change with no data migration.
- Deriving four helpers from one primitive closes a real defect class, evidenced by the prototype's inverted `Predicate` and its `Scalar` that used `FirstOrDefault` with no single-item enforcement.
- Final cutover deletes `FirelyFhirPathProvider`, `FirelyCompiledFhirPath`, and the flag, with no call-site changes.
- Ignixa's FHIRPath conformance is not a gating concern: the official HL7 suite passes 2906 of 2906 across R4/R4B/R5, with nine soft-passes for `conformsTo()` and `%terminologies`, neither of which appears in any search-parameter expression in any supported version.

### Adverse Effects

- **The performance rationale does not apply at this phase, and this must not be cited as if it does.** HTTP ingress (Phase 4) and read codecs (Phase 2b) are still Firely, so every element reaching the indexer is a Firely POCO. Evaluation therefore runs through a per-call `ToIgnixaElement()` adapter and returns through `TypedElementAdapter`. Ignixa's published 3,220x figure is measured on native Ignixa elements and does not describe this path. The benchmark gate for enabling Ignixa in production must measure *adapter-input* evaluation specifically; if it does not show a win, Phase 3 is a correctness-and-seam change only, exactly as Phase 0 was.
- **A process-wide static cannot express per-server configuration.** `TestFhirServerFactory` caches multiple in-process servers, so two in-proc servers configured with different providers cross-contaminate — which is precisely the shape an Ignixa-versus-Firely E2E comparison would take. Constructor injection into `TypedElementSearchIndexer` covers the parity corpus; a genuine per-server FHIRPath provider would require removing the ambient, which in turn requires the ~1,000 direct-`new` converter tests to gain a fixture. Deferred, and the E2E constraint is documented at the static.
- **`$patch` remains Firely-backed until Phase 7** even when the flag says Ignixa. The startup log names the seams the setting actually controls, so this does not silently mislead operators.
- **Reshaping the config node is a breaking change.** An existing scalar `"FhirSdkProvider": "Firely"` binds to the new object type as *nothing*, so an operator who had set `Ignixa` silently reverts to Firely. The direction is fail-safe, and Phase 0's flag is opt-in and unreleased, but the startup log must make the effective values unambiguous.
- **A prerequisite lands in another repository.** The Ignixa adapters passed `Value` through untranslated in both directions, so Firely's `P.DateTime` reached Ignixa's comparison helpers — which narrow operands through a `string`/`DateTime`/`DateTimeOffset` switch and fall through to `null` — turning every date comparison into an empty result instead of a boolean, silently. Fixed in [ignixa-fhir#398](https://github.com/brendankowitz/ignixa-fhir/pull/398); enabling Ignixa in production is blocked on a package release containing it.

### Neutral Effects

- `%context` is not bound by name in Ignixa's `GetEnvironmentVariable`; it falls through to the generic environment dictionary. The evaluation-context bridge binds it explicitly, along with `%resource` and `%rootResource` and the `ElementResolver` that backs `resolve()` — which appears 76 times across the R4, R4B, and R5 search parameters and is the single highest-risk behaviour in the bridge.
- `TypedElementSearchIndexer` moving onto the seam changes two behaviours that were never deliberate: it gains the `ToScopedNode()` wrap the extension path always applied, and its unbounded private cache is replaced by the provider's bounded cache. Both are pinned by characterization tests.
- Custom search parameters are validated through Firely's AST tooling (out of scope) but indexed through Ignixa, so an accept-versus-index mismatch is possible. The parity corpus covers generated parameters; custom-parameter parity is a bake-in observation, not a pre-merge gate.

### Delivery

One PR. The blast radius is roughly 45 files, of which ~27 are single-line `using` swaps, which sits inside ADR 2607's review guardrail once mechanical edits are excluded on the same basis as its solution and Docker entries.

Verification, in order:

1. **Characterization tests** pinning `Select`/`Scalar`/`Predicate`/`IsTrue`/`IsBoolean` and the indexer's current behaviour against Firely 5.11.4 — written and passing before the Ignixa provider exists, so the seam's fidelity is established independently of it.
2. **Parity corpus** — every generated search parameter, every FHIR version, a resource corpus, run through both providers, asserting equal `Select` results and equal `SearchIndexEntry` sets. This is the gate on enabling Ignixa.
3. **Benchmark** of adapter-input evaluation, per the first adverse effect.

Rollback is resetting `CoreFeatures:FhirSdkProvider:FhirPath`. No data migration; index rows written under either provider stay valid, and the parity corpus is what justifies that claim.

# ADR-2606: Spec-compliant date `eq` containment without a temporal `UNION ALL`

**Status**: Proposed
**Date**: 2026-06-26
**Feature**: date-containment-no-union

## Context

FHIR date/time equality (`eq`) on `dbo.DateTimeSearchParam` has two competing behaviors in the codebase:

- **Core emits spec-compliant containment.** `SearchValueExpressionBuilderHelper` builds `DateTimeStart >= lo AND DateTimeEnd <= hi` — the stored period must sit *inside* the query range. This matches the FHIR R4 prefix rule for `eq`: "the range of the search value fully contains the range of the target value" (https://hl7.org/fhir/R4/search.html#prefix).
- **`DateTimeEqualityRewriter` (Core) then weakens it to overlap.** It replaces the two containment predicates with `DateTimeStart <= hi AND DateTimeEnd >= lo`, matching any resource whose period merely *overlaps* the query range. This non-spec overlap is applied system-wide to every date parameter.

To make the overlap model index-friendly for the allow-listed scalar parameter `individual-birthdate`, ADR-2605 introduced `ScalarTemporalEqualityRewriter`, which rewrites an exact-day `eq` into a `UnionExpression` (SQL `UNION ALL`) split on `IsLongerThanADay`. That union is the root cause of recurring SQL-generation failures in chained, `_sort`, and SMART/compartment-join shapes, and has required repeated targeted workarounds. ADR-2605 itself anticipated removal: "the long branch becomes dead once AB#191826 lands; at that point the union collapses to the short branch and this ADR should be revisited."

The key insight: under **containment**, a stored period longer than a one-day query window can never be contained, so `IsLongerThanADay = 1` rows naturally produce zero matches. No split, and therefore no union, is needed. The union only ever existed to serve the overlap model.

## Options Considered

1. **Keep the overlap model and keep patching the union** — continue adding shape-specific workarounds (predicate pushdown, sort-rewriter union handling, chained-skip guards). *(rejected: treats symptoms; the union keeps breaking new query shapes)*
2. **Emit the End-only short predicate unconditionally** — collapse the union to its short branch always. *(rejected: silently changes results for partial-precision stored birthdates — `'1990'`, `'1990-05'` — which overlap matches but End-only/containment drops; this would apply a behavior change with no kill-switch)*
3. **Spec-compliant containment behind a feature flag, with union removal decoupled from the default** — let Core's containment flow to SQL, gate the overlap weakening behind a new flag, and rewrite the scalar rewriter to a single union-free predicate. *(viable — chosen)*

## Decision

We introduce a new SQL configuration flag **`EnableFhirDateContainment`** (in `FhirSqlServerConfiguration`, with the standard config + `FHIRSERVER__…` env-var override) and pair it with the existing **`EnableScalarTemporalEqualityRewriter`** flag. The temporal `UNION ALL` is removed by construction, independent of the flag values.

Gating matrix:

| `EnableScalarTemporalEqualityRewriter` | `EnableFhirDateContainment` | `ScalarTemporalEqualityRewriter` | `DateTimeEqualityRewriter` (overlap) | birthdate `eq` | other date `eq`/`ap` | union |
|---|---|---|---|---|---|---|
| on | on | runs → End-only predicate | skipped | End-only index seek (containment) | containment | none |
| off | on | does not run | skipped | Core containment two-predicate | containment | none |
| on | off | does not run | runs | **legacy overlap (== `main`)** | legacy overlap | none |
| off | off | does not run | runs | legacy overlap | legacy overlap | none |

The rewriter runs **only when both flags are enabled**, and in that state emits a single `IsLongerThanADay = 0 AND DateTimeEnd = endOfDay` `SearchParameterExpression` (the index optimization from ADR-2605) — never a `UnionExpression`. The `DateTimeEqualityRewriter` overlap weakening is skipped **iff** `EnableFhirDateContainment` is on, so Core's containment flows to SQL system-wide for every date parameter.

**Both flags gate the rewriter on purpose.** The End-only predicate is *not* result-equivalent to overlap for partial-precision stored birthdates. `Patient.birthDate = '1990'` is stored as a multi-day range (`IsLongerThanADay = 1`); for `birthdate=eq1990-05-15`, the old union's long/overlap branch matched it, but the End-only predicate does not. End-only therefore equals containment, which differs from overlap. Tying the rewriter to `EnableFhirDateContainment` binds this behavior change to the containment flag, so the off path is a true legacy kill-switch with results identical to `main`.

**Default is `false` (off).** This removes the union out of the box (the rewriter simply never runs), behaves identically to `main` for both SQL and Cosmos, and makes containment an opt-in, one-line flip. We recommend the team review flipping the default to `true` (spec-compliant containment) once the search-result behavior change for partial-precision stored dates has been socialized; the constant is isolated so the flip is trivial. `EnableScalarTemporalEqualityRewriter` is **also defaulted to `false`** here (it shipped `true` on `main`): because the rewriter is now gated behind both flags, its default value is moot while containment is off, and defaulting it off means enabling containment alone yields pure Core containment — the birthdate End-only index optimization becomes a further explicit opt-in rather than implicit.

**Scope is SQL-only.** `DateTimeEqualityRewriter` is shared with the Cosmos pipeline, but the new flag lives in `FhirSqlServerConfiguration` and the union problem is SQL-only. Cosmos stays on overlap; a Core-level containment flag for Cosmos is left as a follow-up. `ap` shares the identical containment expression shape as `eq` by the time `DateTimeEqualityRewriter` runs (the comparator identity is already lost), so it cannot be cleanly isolated and rides the same flag — defensible because `ap` is fuzzy by spec.

## Consequences

- The temporal `UNION ALL` is gone for all flag combinations. The chained/`_sort`/SMART shapes that previously failed SQL generation for birthdate `eq` no longer hit a union, removing an entire class of edge-case workarounds.
- `ScalarTemporalEqualityRewriter` collapses to a single End-only predicate; its `BuildDaySplitUnion` long branch and the `VisitChained` chained-skip guard (a band-aid that only existed because of the union) are deleted, and its visitor simplifies.
- The SMART/compartment-shared union infrastructure (`UnionExpression`, `AppendNewSetOfUnionAllTableExpressions`, `FindRestrictingPredecessorTableExpressionIndex`, the `SearchParamTableExpressionExtensions` union helpers) is untouched — those unions originate in the compartment/SMART rewriters and `SearchOptionsFactory`, not the temporal path.
- With containment on, a day query no longer matches a month/year-precision stored date (overlap → containment). This is the intended, spec-correct change, but it is a **search-result behavior change** that is the reason the default ships off.
- Cosmos remains on overlap, so SQL and Cosmos can diverge when the SQL flag is on. Aligning Cosmos is a tracked follow-up.
- ADR-2605 is superseded: its union optimization is replaced by the union-free containment model it anticipated.

## Verification

The behavior change is validated deterministically without requiring a flag-flippable E2E deployment.

### Coverage map

| Scenario | Flags (`scalar` / `containment`) | `eq` semantics | Proven by |
|---|---|---|---|
| 1 — legacy (== `main`) | any / off | overlap | Existing E2E `DateSearchTests` (`DataStore.All`) + `SqlServerSearchServiceTests` flag-matrix row, and the `LegacyOverlap` combo in `DateEqualityContainmentIntegrationTests`. |
| 2 — containment, no scalar opt | off / on | containment | `SqlServerSearchServiceTests` flag-matrix row (Core containment range survives, no overlap swap, no union) + the `Containment` combo in `DateEqualityContainmentIntegrationTests`. |
| 3 — containment + scalar opt | on / on | containment (birthdate End-only seek) | `ScalarTemporalEqualityRewriterTests` (single predicate, no `UnionExpression`) + the `ContainmentScalar` combo in `DateEqualityContainmentIntegrationTests`. |

`DateEqualityContainmentIntegrationTests` (in `Microsoft.Health.Fhir.Shared.Tests.Integration`, `DataStore.SqlServer`) runs against a real SQL backend in-process. It flips the singleton `FhirSqlServerConfiguration` flags per call, seeds resources through a real `TypedElementSearchIndexer` so `dbo.DateTimeSearchParam` rows reflect true stored precision, isolates each test's data with a unique `_tag`, and asserts the result set for all three combos. The keystone case is a **partial-precision stored birthdate** (`'1980'`, `'1980-05'`, `'1980-05-11'`) queried with an exact day: it matches all three under `LegacyOverlap` and only the exact-day value under both containment combos — the proof that the both-flags gating is correct.

The kept SMART V2 granular-scope union is exercised by `GivenSmartV2ScopeUnion_…`, which drives a search-parameter-scoped `Patient` scope so `SearchOptionsFactory` ANDs a `UnionExpression` (`IsSmartV2UnionExpressionForScopesSearchParameters = true`) beside the user's `birthdate=eq` predicate — the historic worst-case composite shape — and asserts valid SQL plus the spec-correct result set in all three flag states. This confirms the load-bearing union we deliberately keep composes cleanly with containment.

### Appendix — running the existing E2E date suite under all three flag states (reference)

The flags are server-startup config (a `Singleton` bound from `IConfiguration.GetSection("FhirSqlServer")`), not per-request, and the in-process E2E harness has no per-test flag override. A future operator or dedicated CI lane can stand up all three scenarios as follows:

- **Lever:** a custom `StartupBaseForCustomProviders` subclass per scenario that, in `ConfigureServices` after `base.ConfigureServices`, re-registers the singleton with the flags forced (last `AddSingleton` wins). Bind the existing section first to preserve other values, then set `EnableFhirDateContainment` / `EnableScalarTemporalEqualityRewriter`. Do **not** use `FhirSqlServer__…` env vars — they are process-global and the in-proc servers share one test process, so they would leak across scenarios.
- **Isolation:** decorate each scenario startup with `[RequiresIsolatedDatabase]`. `InProcTestFhirServer` then gives it its own database (`InitialCatalog + "_" + startupType.Name`) and `TestFhirServerFactory` caches one server per `(DataStore, startupType)` — three startup types yield three isolated, separately-seeded servers in a single run.
- **SQL-only / in-proc-only caveats:** the flag is a no-op on Cosmos, so scenario-2/3 fixtures must be `DataStore.SqlServer` (never `DataStore.All`). Config injection via custom startup applies only to `InProcTestFhirServer`; a remote/CI `RemoteTestFhirServer` takes flags from its deployment env (default off) and cannot be flipped this way.
- **What differs across scenarios:** scenario 1 vs {2,3} flips every `eq`/`ne` row whose query is finer than a stored value (coarse stored values stop matching a day window); `gt/lt/ge/le/sa/eb` rows are invariant. Scenarios 2 and 3 are result-identical for `DateSearchTests` data (Observation.effective), because the scalar rewriter is allow-listed to `individual-birthdate` only — so scenario 3's distinct value is the birthdate End-only **SQL plan**, not the result set.

Because the default ships off, the existing `DateSearchTests` (`DataStore.All`, overlap) already proves scenario 1 out of the box, and the unit + integration coverage above proves scenarios 2 and 3 — so this E2E expansion is documented as opt-in rather than shipped.

### Known harness limitation

The in-process integration fixture cannot sort by a date search parameter under a `_tag` token filter (pre-existing, orthogonal to this change); `GivenDateEqWithSort` therefore sorts by `_lastUpdated`. Absence of a union under `_sort` is proven at the unit level instead.

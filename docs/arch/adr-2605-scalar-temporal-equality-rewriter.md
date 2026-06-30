# Scalar Temporal Equality Rewriter for Birthdate

## Context

FHIR date `eq` on `dbo.DateTimeSearchParam` has two competing behaviors. Core emits spec-compliant **containment** — `SearchValueExpressionBuilderHelper` builds `DateTimeStart >= lo AND DateTimeEnd <= hi`, so the stored period must sit *inside* the query range (FHIR R4 `eq`: https://hl7.org/fhir/R4/search.html#prefix). `DateTimeEqualityRewriter` (Core) then weakens that to **overlap** (`DateTimeStart <= hi AND DateTimeEnd >= lo`) for every date parameter.

The two-predicate containment shape does not let the optimizer target the table indexes well. Almost every stored birthdate is a single calendar day (`IsLongerThanADay = 0`), served by the general `IX_SearchParamId_StartDateTime_EndDateTime_INCLUDE_IsLongerThanADay_IsMin_IsMax`; the rare wide rows have a dedicated filtered index `..._WHERE_IsLongerThanADay_1`. To keep overlap index-friendly for the allow-listed `individual-birthdate`, this rewriter originally split an exact-day `eq` into a `UnionExpression` (SQL `UNION ALL`) on `IsLongerThanADay`. That union proved to be the root cause of recurring SQL-generation failures in chained, `_sort`, and SMART/compartment shapes.

The insight that removes it: under **containment**, a stored period longer than a one-day window can never be contained, so `IsLongerThanADay = 1` rows match nothing. The day-split — and the union — become unnecessary by construction, leaving only the short-row End-only predicate.

## Options Considered

1. **Keep overlap, keep patching the union** — rejected: treats symptoms; the union keeps breaking new query shapes.
2. **Emit the End-only predicate unconditionally** — rejected: silently changes results for partial-precision stored birthdates with no kill-switch.
3. **Containment behind a feature flag, union removed by construction** — chosen.

## Decision

Add **`EnableFhirDateContainment`** to `FhirSqlServerConfiguration` (config key `FhirSqlServer:EnableFhirDateContainment`, env override `FhirSqlServer__EnableFhirDateContainment`) and pair it with the existing **`EnableScalarTemporalEqualityRewriter`**. The temporal `UNION ALL` is removed regardless of flag values.

| `EnableScalarTemporalEqualityRewriter` | `EnableFhirDateContainment` | `ScalarTemporalEqualityRewriter` | `DateTimeEqualityRewriter` (overlap) | birthdate `eq` | other date `eq` | union |
|---|---|---|---|---|---|---|
| on | on | runs → End-only predicate | skipped | End-only seek (containment) | containment | none |
| off | on | does not run | skipped | Core containment | containment | none |
| on | off | does not run | runs | legacy overlap (== `main`) | legacy overlap | none |
| off | off | does not run | runs | legacy overlap | legacy overlap | none |

The rewriter runs **only when both flags are on**, emitting a single `IsLongerThanADay = 0 AND DateTimeEnd = endOfDay` predicate (the index optimization above) — never a `UnionExpression`. Overlap weakening is skipped iff containment is on, so Core's containment flows to SQL for every date parameter.

`ap` is independent of both flags. Per spec `ap` means *overlap*, so `SearchValueExpressionBuilderHelper` now emits the overlap shape (`DateTimeStart <= hi′ AND DateTimeEnd >= lo′`, widened bounds) for `ap` directly. It never relies on `DateTimeEqualityRewriter`, so skipping that rewriter under containment leaves `ap` unchanged — always overlap, in every flag combination.

Both flags gate the rewriter on purpose: the End-only predicate equals containment, not overlap, for partial-precision stored birthdates (e.g. `birthDate = '1990'`, stored as `IsLongerThanADay = 1`). Tying it to the containment flag keeps the off path a true legacy kill-switch identical to `main`.

**Defaults are `false`.** Off removes the union out of the box and behaves identically to `main` (SQL and Cosmos); containment is a one-line opt-in. `EnableScalarTemporalEqualityRewriter` (shipped `true` on `main`) is also defaulted off here, so enabling containment alone yields pure Core containment and the birthdate End-only optimization stays a further explicit opt-in.

**Scope is SQL-only.** The flag lives in `FhirSqlServerConfiguration`; Cosmos stays on overlap (a Core-level containment flag is a follow-up). `ap` is unaffected: Core emits it as spec overlap directly (see the Decision note above), so only `eq` rides this flag.

## Status

Accepted

Supersedes this rewriter's original `UnionExpression` day-split (an index stopgap) with the union-free containment model above.

## Consequences

- The temporal `UNION ALL` is gone for all flag combinations; the chained / `_sort` / SMART shapes that previously failed SQL generation no longer hit one, retiring a class of workarounds.
- `ScalarTemporalEqualityRewriter` collapses to a single End-only predicate; its day-split union builder and the `VisitChained` chained-skip guard (a band-aid that only existed for the union) are deleted.
- The SMART/compartment-shared union infrastructure (`UnionExpression` and helpers) is untouched — those unions originate in the compartment/SMART rewriters, not the temporal path.
- With containment on, a day query no longer matches a month/year-precision stored date — the intended spec-correct change, and the reason the default ships off.
- Cosmos stays on overlap, so SQL and Cosmos can diverge when the flag is on (tracked follow-up). Tracked alongside AB#191826.

## Verification

| Scenario | Flags (scalar / containment) | `eq` semantics | Proven by |
|---|---|---|---|
| Legacy (== `main`) | any / off | overlap | E2E `DateSearchTests` (`DataStore.All`) + `SqlServerSearchServiceTests.ApplyDateEqualitySemantics` overlap case |
| Containment | off / on | containment | `SqlServerSearchServiceTests.ApplyDateEqualitySemantics` containment case (containment survives, no overlap swap, no union) |
| Containment + scalar | on / on | containment, birthdate End-only seek | `ScalarTemporalEqualityRewriterTests` (single predicate, no `UnionExpression`) |

`SqlServerSearchServiceTests` also asserts no flag combination emits a temporal `UnionExpression`, and `ChainingSearchTests` exercises the chained / `_sort` / reverse-`_has` shapes beside a birthdate `eq` that previously broke SQL generation. The flags are server-startup config and the default ships off, so `DateSearchTests` proves the legacy default out of the box. Flag-flipped E2E coverage and the real-SQL containment + retained-SMART-union integration tests ship in the stacked follow-up PR.

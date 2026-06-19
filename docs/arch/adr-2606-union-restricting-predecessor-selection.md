# ADR-2606: Restricting-Predecessor CTE Selection for Union + Concatenation Pairs

**Status**: Proposed
**Date**: 2026-06-19
**Feature**: scalar-temporal-union-predecessor-fix

## Context

ADR-2605 made `ScalarTemporalEqualityRewriter` emit a `UnionExpression` (split on `IsLongerThanADay`) for exact-day birthdate equality. When that union is followed by a `Normal` + `Concatenation` date-range pair (produced by `DateTimeBoundedRangeRewriter` / `ConcatenationRewriter`), `SqlQueryGenerator` chained the wrong predecessor CTE: the `Concatenation` branch joined its own `Normal` sibling instead of the shared union-aggregate CTE that both branches must restrict against.

The root cause was that `FindRestrictingPredecessorTableExpressionIndex` mixed two coordinate systems — the generated-CTE counter (which a union advances by N) and indices into the unsorted logical `SearchParamTableExpressions` list. A union inflating the counter tripped the out-of-range guard before the `Concatenation` recursion could skip its sibling, so the join target was wrong whenever a union preceded the pair.

## Options Considered

1. **Stop emitting the union** — drop the ADR-2605 optimization to avoid the broken shape *(rejected: discards a real index-targeting win and the underlying generator bug would remain for any future union)*.
2. **Patch the existing finder's guard** — reconcile the counter/logical-index mismatch inline *(rejected: keeps two coordinate systems in play; fragile and hard to reason about as more rewriters emit unions)*.
3. **Track generation order explicitly** — record `(Kind, OutputCteIndex)` for each emitted CTE in a side list and resolve predecessors by walking that sequence *(rejected: introduced a regression — see below)*.
4. **Resolve predecessors from the CTE counter** — drop the logical-index list entirely and key off `_tableExpressionCounter`, which already reflects every emitted CTE *(viable, chosen)*.

Option 3 was implemented first but broke SmartV2 scope searches with includes/rev-includes (`Invalid object name 'cteN'`). The side list recorded only top-level loop expressions, while the include and SmartV2 re-generation paths emit additional CTEs (the filtered-data CTE; union CTEs re-appended after the counter is reset and restored) that advance `_tableExpressionCounter` without a corresponding list entry. That desync made list position diverge from real CTE numbering, so predecessor lookups returned a stale or forward CTE index.

## Decision

We resolve the restricting predecessor purely from `_tableExpressionCounter` — the CTE index of the expression being generated right now. For every kind the predecessor is the immediately preceding CTE (`_tableExpressionCounter - 1`), **except** `Concatenation`, the second branch of a `(Normal, Concatenation)` UNION ALL pair: its `Normal` sibling always occupies exactly one CTE, so it skips the sibling (`_tableExpressionCounter - 2`) and both branches restrict against the sibling's predecessor — the shared union-aggregate CTE when a union precedes the pair.

The counter is incremented by every path that emits a CTE, so it stays authoritative across union expansion, SmartV2 include re-generation, and the include filtered-data CTE. This is byte-identical to the original generator for all non-`Concatenation` cases (resolving the regression), and corrects the `Concatenation` join target that ADR-2605's union exposed. It is a SQL-generation correctness fix only — matched resources and the date-range overlap behavior described in ADR-2605 are unchanged.

## Consequences

- Union output followed by a date-range pair now produces correct JOIN targets; predecessor selection no longer depends on whether an upstream rewriter inflated the CTE counter.
- Non-`Concatenation` predecessor resolution matches the pre-union generator exactly, so include / rev-include / SmartV2 paths are unaffected by this change.
- Predecessor resolution no longer maintains a parallel generation-order list, removing a coordinate system that had to stay in sync with every CTE-emitting path.
- The fix relies on the invariant that a `Concatenation` branch is emitted immediately after its single-CTE `Normal` sibling; the `SortExpressionsByQueryLogic` ordering that guarantees this is documented at its source and covered by a focused unit test.

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
3. **Track generation order explicitly** — record the order CTEs are emitted and resolve predecessors from that single authoritative sequence *(viable, chosen)*.

## Decision

We record generation order in `_generatedTableExpressions` (`(Kind, OutputCteIndex)` per emitted CTE) and resolve the restricting predecessor by walking that sequence. A `Concatenation` branch resolves to its `Normal` sibling's predecessor, so both date-range branches restrict against the shared union-aggregate CTE. This replaces the counter-vs-logical-index arithmetic with one coordinate system. The `SortExpressionsByQueryLogic` ordering invariant the walk depends on (unions move first; a `(Normal, Concatenation)` sibling pair stays adjacent and ordered) is documented at its source.

This is a SQL-generation correctness fix only. Matched resources and the date-range overlap behavior described in ADR-2605 are unchanged.

## Consequences

- Union output followed by a date-range pair now produces correct JOIN targets; predecessor selection no longer depends on whether an upstream rewriter inflated the CTE counter.
- Future rewriters that emit unions inherit correct predecessor chaining without revisiting this code path.
- The predecessor walk now relies on the `SortExpressionsByQueryLogic` ordering invariant; changes to that ordering must preserve it (captured in code remarks and a focused unit test).

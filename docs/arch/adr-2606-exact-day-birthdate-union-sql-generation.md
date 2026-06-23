# ADR-2606: SQL Generation Correctness for the Exact-Day Birthdate Union

**Status**: Proposed
**Date**: 2026-06-22
**Feature**: scalar-temporal-union-predecessor-fix

## Context

ADR-2605 made `ScalarTemporalEqualityRewriter` rewrite exact-day birthdate equality into a `UNION ALL` so each branch hits a tight, index-friendly date range. That union exposed two latent defects in `SqlQueryGenerator`'s CTE lowering. They are fixed together because Part 2 (chain-aware lowering) depends on Part 1 (predecessor selection).

- **Part 1 — predecessor CTE selection.** When the union is followed by a `Normal` + `Concatenation` date-range pair, `FindRestrictingPredecessorTableExpressionIndex` chose the wrong predecessor: it mixed the generated-CTE counter (which a union advances by N) with indices into the unsorted logical expression list, so the `Concatenation` branch joined its own `Normal` sibling instead of the shared union-aggregate CTE.
- **Part 2 — chain-aware lowering.** A `VisitChained` guard had suppressed the ADR-2605 rewrite for chained birthdate because the union-lowering path assumed top-level unions only. Removing the guard (so chained birthdate gets the optimization) produced broken SQL and production 500s (ICMs 21000001063947, 815288838): unions were hoisted ahead of their chain link, branches carried no chain-link predecessor, and the top-level union state machine misrouted a second same-target predicate onto the chain source (`T1/Sid1`) instead of the target (`T2/Sid2`).

## Decision

### Part 1 — Resolve the predecessor from the CTE counter

**Options considered**

1. **Stop emitting the union** *(rejected: discards the index win and leaves the generator bug for any future union)*.
2. **Patch the existing finder's guard inline** *(rejected: keeps two coordinate systems in play; fragile)*.
3. **Track generation order in a side list** *(rejected: desynced from `_tableExpressionCounter` on SmartV2 include/rev-include paths, producing `Invalid object name 'cteN'`)*.
4. **Key off `_tableExpressionCounter`** *(chosen)*.

Resolve the predecessor purely from `_tableExpressionCounter`, the index of the CTE being generated now. The predecessor is `_tableExpressionCounter - 1` for every kind except `Concatenation`, whose single-CTE `Normal` sibling is skipped (`- 2`) so both branches restrict against the sibling's predecessor — the shared union aggregate when a union precedes the pair. The counter already reflects every emitted CTE, so this is byte-identical to the original generator for all non-`Concatenation` cases and corrects the `Concatenation` join target.

### Part 2 — Make union lowering chain-aware

**Options considered**

1. **Keep the `VisitChained` guard** *(rejected: leaves chained birthdate unoptimized and the generator latently broken)*.
2. **Hoist the union, restrict the chain link against it** *(rejected: inverts the established chain-link-first contract)*.
3. **Chain-link-first with chain-aware branches** *(chosen)*.

Remove the guard and gate the top-level union logic on `ChainLevel`. `SortExpressionsByQueryLogic` hoists only `ChainLevel == 0` unions, so a chain-nested union stays after its chain link. `AppendNewSetOfUnionAllTableExpressions` propagates the parent `ChainLevel` and pins every branch to the shared chain-link predecessor; at `ChainLevel > 0` branches join it on the target columns (`T2/Sid2`) exactly as a plain chained predicate does. The top-level/SMART union state (`_unionVisited`, `_firstChainAfterUnionVisited`, `_unionAggregateCTEIndex`, and the "previous union" join) is likewise gated to `ChainLevel == 0`, so a downstream same-target predicate falls through to the plain chained path and intersects the aggregate on `T2/Sid2`. `ChainLevel == 0` output is byte-identical, leaving SMART-scope, `_type`, and compartment unions unaffected; forward and reverse (`_has`) chains share this path.

Both parts are SQL-generation correctness fixes only; matched resources and the ADR-2605 date-range overlap behavior are unchanged.

## Consequences

- Predecessor selection no longer depends on whether an upstream rewriter inflated the CTE counter, and no longer maintains a parallel generation-order list; non-`Concatenation` resolution matches the pre-union generator exactly, so include / rev-include / SmartV2 paths are unaffected.
- Chained and reverse-chained exact-day birthdate now emit correct SQL and gain the index optimization; the production 500s are resolved.
- The shared SQL-generation path gains a small amount of chain-level branching, gated so existing `ChainLevel == 0` union shapes are a no-op.
- Part 1 relies on a `Concatenation` branch following its single-CTE `Normal` sibling; this ordering is enforced by `SortExpressionsByQueryLogic` and covered by a focused unit test.
- Regression coverage was added at the unit level (Concatenation predecessor target; forward/reverse chained union structure; second same-target predicate joining on `T2/Sid2`) and end-to-end (the customer shape), so the removed guard is continuously validated in CI.

# ADR-2606: Chain-Aware Union Lowering for Chained Exact-Day Birthdate

**Status**: Proposed
**Date**: 2026-06-22
**Feature**: scalar-temporal-union-predecessor-fix

## Context

ADR-2605 made `ScalarTemporalEqualityRewriter` rewrite exact-day birthdate equality into a `UNION ALL` (split on `IsLongerThanADay`) so each branch hits a tight, index-friendly date range. A conservative `VisitChained` guard (PR #5607) suppressed that rewrite whenever the birthdate appeared inside a chained or reverse-chained expression, because the union-lowering path in `SqlQueryGenerator` was written exclusively for top-level unions.

That guard left chained exact-day birthdate on the unoptimized path and, more importantly, masked a latent generator bug: when the guard was removed so chained birthdate could share the optimization, the lowering produced **broken SQL** and HTTP 500s in production (ICMs 21000001063947 and 815288838 — `MedicationDispense?patient:Patient.birthdate=<day>&_sort=-whenhandedover&patient:Patient.identifier=...`). Three top-level-only assumptions were responsible: `SortExpressionsByQueryLogic` hoisted *every* union to the front regardless of chain nesting (emitting the chain-nested union before its own chain link); `AppendNewSetOfUnionAllTableExpressions` created each branch at the default `ChainLevel 0` with no chain-link predecessor; and `HandleParamTableUnion` unconditionally emitted bare `T1/Sid1` branches with no predecessor JOIN, so the downstream chain link restricted the wrong resource IDs.

## Options Considered

1. **Keep the `VisitChained` guard** — ship only the non-chained union fix; chained birthdate stays on the safe pre-rewriter path *(rejected: leaves chained birthdate without the index optimization and keeps the generator latently broken for any future chain-nested union)*.
2. **Hoist-the-union (chain-link-after)** — let the union emit first, then make the chain link restrict against the union aggregate *(rejected: inverts the established chain-link-first contract and forces every chain consumer to special-case union predecessors)*.
3. **Chain-link-first, chain-aware branches** — keep `[chain link, union]` order, gate union hoisting on `ChainLevel == 0`, and make every union branch restrict against the shared chain-link predecessor exactly as a plain chained predicate does *(viable, chosen)*.

## Decision

We make union lowering chain-aware (Option 3) and remove the `VisitChained` guard. `SortExpressionsByQueryLogic` only hoists `ChainLevel == 0` unions, leaving a chain-nested union in place so its chain link still generates first. `AppendNewSetOfUnionAllTableExpressions` propagates the parent `ChainLevel` to each branch and pins a single restricting predecessor — the chain link at `firstInclusiveTableExpressionId - 1` — shared by all branches. At `ChainLevel > 0`, branches join that pinned predecessor on the chain *target* columns (`T2`/`Sid2`) and carry the chain columns through, identical to how a plain chained birthdate predicate restricts; the aggregate CTE already does `SELECT *`, so all four columns flow and the final query reads `T1`/`Sid1`. Forward and reverse (`_has`) chains share this path because the baseline chained-predicate join always targets `T2`/`Sid2`. For `ChainLevel == 0` the behavior is byte-identical to before, so SMART-scope, `_type`, and compartment unions are unaffected.

## Consequences

- Chained and reverse-chained exact-day birthdate now emit correct SQL and gain the same index optimization as the top-level case; the production 500s are resolved.
- The hottest path in the SQL generator is now chain-level aware, with the chain-nested union pinned to the chain link rather than the previous branch — a small amount of added branching on a shared path, gated so existing union shapes are a no-op.
- Behavior is unchanged for matched resources and date-range overlap (ADR-2605); this is a SQL-lowering correctness fix layered on the predecessor-selection fix in the companion ADR-2606 (union restricting-predecessor selection).
- Regression coverage was added at the SQL-generation unit level (forward and reverse chained union branch structure) and end-to-end (chained exact-day birthdate combined with a base-resource sort and a second chained predicate — the customer shape), so the removed guard is now continuously validated in CI.

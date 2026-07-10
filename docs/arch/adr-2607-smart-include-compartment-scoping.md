# ADR-2607: Enforce SMART Patient Compartment Scoping on `_include` / `_revinclude` (SQL)

**Status**: Proposed
**Date**: 2026-07-10
**Feature**: smart-include-compartment-leak

Labels: [Security](https://github.com/microsoft/fhir-server/labels/Security) | [Area-SMART](https://github.com/microsoft/fhir-server/labels/Area-SMART) | [Area-Search](https://github.com/microsoft/fhir-server/labels/Area-Search)

## Context

An MSRC report showed that a SMART-on-FHIR patient-scoped caller (a token such as `patient/*.read` confined to a single compartment, e.g. `Patient/CHILD`) can read resources **outside** its patient compartment by abusing search inclusions. The primary search is correctly compartment-restricted, but the `_include` / `_revinclude` expansion is not: the included/revincluded resources are resolved by plain reference joins with no compartment predicate. A caller can therefore start from an in-compartment resource and pull in resources belonging to *other* patients — a PHI disclosure.

The compartment restriction is applied to the *matched* rows of a search, but the SQL that materializes includes (built in `SqlQueryGenerator.HandleTableKindInclude`) joins `ReferenceSearchParam` to `Resource` and returns whatever is referenced, unfiltered. Any reference that crosses a compartment boundary leaks.

Constraints:
- **SQL data provider only.** Cosmos DB is being deprecated and is explicitly out of scope for this fix.
- Must not regress existing SMART include/revinclude behavior, including wildcard (`_revinclude=*`) and iterate cases, nor the SMART V2 fine-grained (`ApplyFineGrainedAccessControlWithSearchParameters`) path, which already constrains includes by scope through its own union rewrite.
- Must not collide with the query-plan / custom-query hash cache.

### Reproduction

Two integration tests were added to `SmartSearchTests` (Shared integration suite, run against a real SQL Server) before any fix, and both **failed** (confirming the leak):

1. **`_include`** — a patient-scoped caller searches a resource in its compartment with an `_include` whose target resource belongs to a different patient, and asserts the out-of-compartment target is **not** returned.
2. **`_revinclude`** — the symmetric case: a `_revinclude` that pulls in resources referencing an out-of-compartment resource, asserting they are **not** returned.

These two tests are the RED baseline; the fix turns them GREEN while the pre-existing SMART include/revinclude tests stay GREEN.

## Options Considered

1. **Post-filter in Core/API after retrieval** — drop out-of-compartment includes after the SQL result is read back. *(rejected: the PHI has already crossed the storage boundary into server memory; also breaks `_total`, paging, and the `$includes` continuation contract.)*
2. **Core expression rewriter that rewrites each include into a compartment-scoped sub-search** — express the constraint provider-agnostically in the Core expression tree. *(viable, but heavy: include expansion is emitted as a single generated statement in the SQL layer, SMART include handling already lives there, and a Core rewrite would have to reproduce the include CTE structure.)*
3. **Hand-rolled SQL predicate via the combined compartment search parameter (`clinical-patient`)** — require the produced resource to carry a `ReferenceSearchParam` row for the combined compartment parameter pointing at the compartment id. *(rejected after investigation: the common/combined search parameters that `CompartmentDefinition` maps to — `clinical-patient` and friends — are **never materialized as index rows**. Patient references are indexed only under type-specific parameters such as `Observation-subject`. A predicate keyed on the combined parameter matches nothing and drops legitimate in-compartment resources.)*
4. **Reuse the `dbo.CompartmentAssignment` membership table** — join the produced rows against the compartment-assignment table that older compartment search used. *(rejected: `CompartmentAssignment` is **deprecated** and must not be used by new code.)*
5. **Reuse the SMART compartment UNION the primary query already builds, and intersect it with the produced include/revinclude rows** — the same `SmartCompartmentSearchExpression` → UNION-of-CTEs mechanism that scopes the primary search is extended to the included rows. *(chosen: no new membership store, no deprecated table, and it composes with SMART V1 and V2 — including granular scopes.)*

## Decision

Reuse the compartment restriction the server **already** builds for the primary search rather than inventing a second membership check. `SearchOptionsFactory` emits a `SmartCompartmentSearchExpression`; the SQL rewrite (`SmartCompartmentSearchRewriter` / `SqlCompartmentSearchRewriter`) turns it into a set of per-resource-type CTEs — each keyed on a **materialized, type-specific reference parameter** that points at the compartment id — `UNION`ed into a single "compartment membership" CTE that is then intersected with the primary query. This is the mechanism the user asked us to follow; the leak exists only because that intersection was never applied to includes.

The fix extends that same union to the produced rows of `_include` / `_revinclude`:

- **Broaden the union's covered types.** The compartment union is built over the primary search types *and* the types produced by include/revinclude expansion (`primary ∪ produced`), so the membership CTE enumerates the included resource types. With no includes this set equals the primary types, so non-include queries are byte-for-byte unchanged. A reversed wildcard `_revinclude=*:*` under an unrestricted scope produces an open-ended type set (there is no bound list of types that may reference the compartment root); this is represented as *all* compartment resource types so every revincluded type is covered.
- **Enumerate membership through materialized reference parameters.** For a SMART compartment the union additively includes, for each compartment resource type, every materialized reference parameter that targets the compartment root type — not only the parameter named in the `CompartmentDefinition`. This is required because the definition maps several clinical types (e.g. `Encounter`, `Condition`, `Procedure`, `ImagingStudy`) to the combined `clinical-patient` parameter, which is never materialized (Option 3); the resources are indexed only under type-specific parameters such as `Encounter-subject`. The addition is safe: every union member is still constrained to `ReferenceResourceId = compartmentId`, so it can only admit resources that reference the compartment root itself — precisely the SMART model of "any resource that refers to the patient."
- **Re-generate and intersect at the include.** Includes are emitted in a separate statement (`INSERT INTO @FilteredData`, then a new `;WITH`). The compartment union is re-generated inside that statement — mirroring the existing SMART V2 scope-union handling — and applied as an `EXISTS` intersection: the include **target** (for `_include`) or the include **source** (for `_revinclude`) must appear in the compartment membership CTE.

This composes across SMART versions. A V1 request, or a V2 request with granular scopes but no per-scope search parameters, carries only the compartment union, which is re-applied to includes. When a V2 fine-grained scope union is *also* present, both unions are re-generated and intersected independently, so a produced row must satisfy the compartment **and** the scope. The compartment intersection is applied even when the scope grants all resource types (`patient/*.read`), because the compartment — not the scope — is the confidentiality boundary. Because the predicate adds real SQL text (and the compartment id is a hashed parameter), the generated query gets a distinct query hash and cannot collide with non-compartment custom queries in the plan/hash cache.

## Consequences

- Closes the MSRC disclosure on the SQL provider for SMART V1 and V2 (including granular scopes): `_include` / `_revinclude` can no longer return resources outside the caller's patient compartment.
- Reuses the authoritative, already-tested compartment union — a single notion of "in compartment," consistent with primary compartment search — instead of a second divergent membership check, and avoids both the deprecated `CompartmentAssignment` table and the never-materialized combined-parameter predicate.
- **Correctness depends on the union enumerating membership through the materialized type-specific reference parameters** — the same index rows the primary compartment search relies on. Compartment resource types whose definition resolves only to a non-materialized combined parameter are not captured by reference alone; keeping the union's parameter resolution aligned with what the indexer actually writes is a security-sensitive invariant, guarded by the reproduction and regression suites.
- **Side effect: the base SMART compartment result is now more complete.** Because the union enumerates materialized type-specific reference parameters, a patient's own resources that were previously dropped by the non-materialized `clinical-patient` mapping (e.g. an `Encounter`, `ImagingStudy` or `Procedure` linked only via `subject`) are now correctly returned. This is a safe widening (still bounded to resources referencing the compartment root) but it does change result counts; one integration assertion that hard-coded the old undercount was updated accordingly.
- Adds an `EXISTS` sub-predicate and a re-generated union CTE to include/revinclude statements. Cost is bounded: the produced-row set is small and the membership CTE reuses the `ReferenceSearchParam` index.
- Include/revinclude of genuinely shared, non-patient-specific reference targets (e.g. `Practitioner`, `Organization`, `Location`, `Medication`) must remain available; these are represented in the compartment union's universal-type members so they are not dropped.
- **SQL-only.** Cosmos is deliberately untouched (deprecated). If Cosmos SMART includes need the same guarantee later, it is a separate follow-up.

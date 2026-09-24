# ADR-2607: Enforce SMART Patient Compartment Scoping on `_include` / `_revinclude` (SQL)

**Status**: Proposed
**Date**: 2026-07-10 (revised 2026-08-11)
**Feature**: smart-include-compartment-leak

Labels: [Security](https://github.com/microsoft/fhir-server/labels/Security) | [Area-SMART](https://github.com/microsoft/fhir-server/labels/Area-SMART) | [Area-Search](https://github.com/microsoft/fhir-server/labels/Area-Search)

## Context

A SMART-on-FHIR patient-scoped caller (for example a token confined to `Patient/CHILD`) can read resources **outside** its compartment by abusing search inclusions. The primary search is correctly compartment-restricted, but `_include` / `_revinclude` expansion is not: included resources are resolved by plain reference joins with no compartment predicate. A caller can start from an in-compartment resource and pull in resources belonging to other patients. The same gap applies to the `$includes` continuation operation.

Constraints:

- **SQL data provider only.** Cosmos DB is deprecated and explicitly out of scope.
- Must not regress existing include/revinclude behavior, including wildcard and `:iterate` cases, nor the SMART V2 fine-grained scope path.
- Must not materialize the compartment: a patient compartment can hold millions of resources, so a per-request table of authorized keys is not viable.
- Must not collide with the query-plan / custom-query hash cache.
- Must preserve the existing `$includes` continuation-token format and paging semantics.

## Options Considered

1. **Post-filter in Core/API after retrieval** — drop out-of-compartment includes after SQL returns them. *(rejected: PHI has already crossed the storage boundary; also breaks `_total`, paging and the `$includes` contract.)*
2. **Core expression rewriter that rewrites each include into a compartment-scoped sub-search** — express the constraint provider-agnostically. *(rejected: include expansion is emitted as a single generated statement in the SQL layer; a Core rewrite would have to reproduce the include CTE structure.)*
3. **Reuse the deprecated `dbo.CompartmentAssignment` membership table.** *(rejected: the table is deprecated and must not be used by new code.)*
4. **Regenerate the primary compartment UNION inside the include statement and intersect it with produced rows.** *(rejected after prototyping: a CTE cannot cross the statement boundary, so the whole union is emitted twice; wildcard includes expand it across many type/parameter combinations, inflating compilation time and memory, and it requires mutable union-tracking state threaded through the query generator.)*
5. **Authorize each include candidate directly against `ReferenceSearchParam`.** *(chosen.)*

A visual comparison of the generated SQL for options 4 and 5 is in [`docs/flow diagrams/smart-include-candidate-authorization-comparison.excalidraw`](../flow%20diagrams/smart-include-candidate-authorization-comparison.excalidraw).

## Decision

Authorize the resources **produced** by each include branch rather than enumerating the compartment. For each candidate the generated SQL asserts one of: the candidate is the compartment root itself; the candidate's type is universally shared (Location, Organization, Practitioner, Medication); or an `EXISTS` semi-join finds a `ReferenceSearchParam` row for that candidate keyed to a formal compartment membership parameter and pointing at the compartment root. Resource types with conditional visibility (currently Device, which is visible only when unassigned or assigned to the compartment root) contribute additional legs.

Membership rules are **exactly** the parameters nominated by the FHIR `CompartmentDefinition` — not every reference parameter that can target Patient. `Observation.focus` references a patient but is not a Patient compartment parameter, so it must never confer membership; the same applies to custom Patient-targeting search parameters. All nominated parameters are materialized by the indexer, including `resolve()`-filtered ones such as `clinical-patient`, because the indexer evaluates `resolve()` through `LightweightReferenceToElementResolver`. No substitution or equivalence mapping is required, and none should be reintroduced: substituting sibling parameters silently widens the compartment beyond the specification.

The rules are lowered once per request into an immutable membership descriptor carried on `SqlRootExpression`, keyed by search-parameter URL rather than numeric ID so it stays independent of schema and FHIR version. Because the descriptor rides outside the visitable expression tree, two fail-closed guards refuse to generate include SQL if it is ever lost — a compartment-bound request that produces no descriptor throws, and the query generator re-checks a flag on the search options. Both the compartment union used by the primary search and this candidate predicate are built from the same rule source, so the two paths cannot drift.

Ordering within each include branch is a correctness requirement, not an optimization: authorization must be applied **before** the branch's `TOP`. If unauthorized candidates were allowed to consume a branch's page allowance, pages would be under-filled, authorized resources would be skipped, and continuation links would advance past them. Authorization is re-evaluated on every `$includes` page — a continuation token is never treated as proof that a resource is still authorized, so following a related link with a different caller's token reapplies that caller's compartment and scopes.

Compartment authorization and SMART V2 scope authorization remain independent intersections: an included resource must satisfy the include relationship **and** compartment membership **and** any V2 scope restriction. The compartment intersection applies even when the scope grants all resource types, because the compartment — not the scope — is the confidentiality boundary.

## Consequences

- Compartment scoping is now enforced on the SQL provider for SMART V1 and V2, on initial include pages and on every `$includes` continuation page.
- Work scales with the number of include candidates rather than compartment size. The predicate seeks `ReferenceSearchParam` on its clustered `(ResourceTypeId, ResourceSurrogateId, …)` prefix, and `EXISTS` prevents a candidate with several qualifying references from being duplicated. Generated SQL is materially smaller than the rejected union-regeneration approach.
- Membership is a single, testable notion of "in compartment" shared by the primary search and includes, with no deprecated table and no mutable union-tracking state in the query generator.
- **Test fixtures must index with the same `resolve()` support as the production server.** The SMART integration fixture previously indexed with a stub reference resolver, so every `resolve()`-filtered compartment parameter produced no index rows and compartment results under-returned in a way that never occurred in production. Correctness work was nearly built on that artifact. `SmartCompartmentMembershipMaterializationTests` now indexes a real resource per compartment type and asserts its membership parameters are genuinely materialized, so a future FHIR version or search-parameter change that breaks this fails loudly instead of silently under-returning.
- One visible behavior change: patient-scoped callers now correctly receive in-compartment resources whose membership is established through `clinical-patient` (for example Immunization), which the previous fixture masked. One integration count assertion was updated accordingly.
- Adds a correlated `EXISTS` per include candidate. The worst case is a `_revinclude` with very high inbound-reference fan-out where most candidates are unauthorized; this needs plan measurement at production scale, and join hints should not be added without that evidence.
- **SQL only.** Cosmos is deliberately untouched. Iterative includes still return a truncation warning rather than a `$includes` link; making them pageable is a separate design. Applying the same candidate-driven strategy to SMART V2 scope unions is a possible follow-up.

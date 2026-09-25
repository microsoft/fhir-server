# ADR-2609: Fail Closed When SMART Authorization Cannot Be Enforced

**Status**: Proposed
**Date**: 2026-09-14
**Feature**: smart-authorization-fail-closed

Labels: [Security](https://github.com/microsoft/fhir-server/labels/Security) | [Area-SMART](https://github.com/microsoft/fhir-server/labels/Area-SMART) | [Area-Search](https://github.com/microsoft/fhir-server/labels/Area-Search)

## Context

SMART-on-FHIR authorization is not enforced by a dedicated access-control layer. It is enforced through the search pipeline, in two distinct ways:

- **By injected predicate.** A clinical scope's search-parameter restriction (for example `patient=<id>` on `patient/Observation.rs?patient=<id>`) is merged into the caller's query and parsed like any other query parameter, and the SMART compartment is lowered into an expression union. The authorization is only real if those expressions survive parsing and reach the generated SQL.
- **By inference from the search index.** The SMART Device restriction introduced in [ADR-2607](adr-2607-smart-include-compartment-scoping.md) treats a Device with no patient reference as unassigned, and therefore visible in every compartment. It detects "no reference" from the *absence* of a `Device.patient` row in `ReferenceSearchParam`.

Both mechanisms have an implicit dependency that was never stated: the search parameters they rest on must be available. The server allows search parameters to be disabled, marked pending delete, or re-enabled and left awaiting a reindex, and it deliberately continues serving requests in those states. Crucially, `SearchParameterStatusManager.EvaluateSearchParamStatus` distinguishes *supported* from *searchable*: `IsSupported` covers both `Supported` and `Enabled`, while `IsSearchable` is set only for `Enabled`. Indexing writes the **supported** parameters (`TypedElementSearchIndexer` resolves through `ISupportedSearchParameterDefinitionManager`) and querying resolves the **searchable** ones (`ExpressionParser` resolves through `SearchableSearchParameterDefinitionManager`), so during those windows the index is legitimately incomplete relative to what a query may reference.

The general search pipeline already has a well-established answer for an unavailable parameter: `SearchParameterNotSupportedException`, resolved by `Prefer: handling` into either a warning (lenient, the default) or a `400` (strict). That contract is right for a user-supplied *filter* — a filter the caller asked for and did not get is the caller's problem. It is wrong for an *authorization predicate*, because the same "drop it and warn" behavior silently widens the caller's effective scope. The two kinds of predicate were indistinguishable once merged into `SearchParams`, so both were handled by the filter contract.

The tension this ADR resolves: the server's availability posture says "degrade gracefully and keep serving", while its security posture says "never serve data the caller is not entitled to." Where an authorization predicate is involved, these conflict, and the code had silently resolved the conflict in favor of availability.

Constraints:

- Must not change behavior when search parameters are enabled, which is the normal state.
- Must not alter the lenient/strict contract for search parameters the caller supplied.
- Must keep the compartment union and the SQL include/revinclude candidate predicate driven by one rule set, per [ADR-2607](adr-2607-smart-include-compartment-scoping.md), so the two paths cannot drift.
- SQL data provider only. Cosmos does not implement the Device restriction.

## Options Considered

1. **Leave the filter contract in place and rely on operators not disabling authorization-relevant parameters** — document the requirement instead of enforcing it. *(rejected: the safety of an access-control boundary would depend on operational discipline, and the failure is silent — a widened scope is indistinguishable from a correct response in the payload, in logs and in metrics.)*
2. **Treat strict handling as the security control** — require `Prefer: handling=strict` for SMART requests. *(rejected: leniency is the default and is client-controlled, so the caller would choose whether the server enforces authorization. An authorization check may not be relaxable by a request header.)*
3. **Reject the request at the entry point when any authorization-relevant parameter is unavailable** — pre-check the definition manager before parsing. *(rejected: it duplicates knowledge of which parameters each scope and compartment will need, and it drifts as soon as a new authorization predicate is added. It also cannot see constraints that fail to parse for reasons other than availability.)*
4. **Block the search-parameter status transition itself** — refuse to disable a parameter that any authorization path depends on. *(rejected as the sole measure: it does not help a parameter that is `Supported` while a reindex runs, which is a legitimate transient state with an incomplete index, and it cannot cover custom scopes. Worth revisiting as defense in depth.)*
5. **Make the authorization predicates verify their own enforceability and deny the request when they cannot be enforced** — carry the distinction between an injected authorization predicate and a user filter, and make authorization-relevant index inferences check that the index they read is complete. *(chosen.)*

## Decision

We will treat the enforceability of a SMART authorization predicate as part of the authorization check itself, and deny the request when it cannot be enforced. Concretely, three invariants are now stated and enforced rather than assumed.

**An injected scope constraint is a mandatory predicate, not a filter.** Constraints that SMART scopes merge into the caller's query are tracked as they are injected and verified after parsing. If one did not survive — whether it was dropped by the resource-specific scope path or demoted into the unsupported-parameter set by the wildcard path — the request is denied with `InvalidSearchOperationException`, which surfaces as `403 Forbidden`. This is deliberately independent of `Prefer: handling`: leniency may not relax an authorization check. Constraints are matched on the exact `(name, value)` pair, so a similarly named parameter supplied by the caller is not mistaken for the injected one and keeps today's lenient/strict behavior.

**Authorization-relevant absence is never inferred from an index that is not guaranteed complete.** The Device restriction now resolves to one of three states — not applicable, enforceable, or unenforceable — using `IsSearchable` rather than mere definedness. `IsSearchable` is the correct gate precisely because it means `Enabled`, and therefore also means the index is complete; a parameter that is disabled, pending delete, or re-enabled and awaiting reindex is not searchable and its missing rows carry no information. Note the lookup that decides whether the restriction *applies* still uses the unfiltered definition manager, because "this FHIR version has no Device patient linkage" and "the linkage exists but is unavailable" are different situations with different correct answers.

When the restriction is unenforceable we fail closed by emitting a conditional rule whose visibility is `Never`. This is a two-sided construction and both sides matter: the rule contributes no authorizing predicate, and because `GetSharedResourceTypes` subtracts any type carrying a conditional rule, Device also stays out of the unconditionally shared types. Device therefore becomes **invisible** within the compartment until `Device.patient` is enabled and reindexed. Dropping the rule entirely would return Device to the universal set and make it visible to everyone — the opposite of the intent — and authorizing the "unassigned" branch would expose devices assigned to other patients, since those can also lack an index row. `Never` rules are removed before reaching the SQL generator, which authorizes on a two-valued distinction, and the generator additionally ignores any visibility it does not explicitly recognize so a future value cannot fall through into the authorizing branch.

**A read returns the resource that was requested, or nothing.** A SMART read is executed as an `_id` search so that authorization filters apply. The result is only a valid answer to "read this resource" if it actually contains that resource, so the handler now requires the returned entry to match the requested type and id instead of taking the first entry. We chose an output identity guard over an entry-point availability check because it enforces the contract for every cause, not just for an unavailable `_id`.

## Consequences

- Each of the three paths now has a stated contract that a test can assert, replacing three implicit assumptions about search-parameter availability. The SMART Device path is additionally covered by a mutation-verified test: removing the `Never` filter from the SQL membership context makes it fail.
- The reasoning generalizes beyond this change and is the rule to apply to future SMART work: an authorization decision may depend on the *presence* of an index row, never on its *absence*, unless the index is known to be complete. Any new conditional-visibility rule that reads absence must gate on `IsSearchable` and fail closed.
- **`403` becomes reachable for configuration reasons rather than caller behavior.** A SMART caller whose scope depends on a disabled or reindexing parameter is denied until the parameter is enabled and its reindex completes. This is intended, but it is an availability regression relative to the previous behavior and it is the change most likely to surprise an operator. It should be called out in release notes, and the accompanying message names the parameter so the cause is diagnosable without reproducing the request.
- Disabling or reindexing a search parameter is no longer a purely search-scoped operation when SMART is enabled: it can withdraw access. Operators need to treat `Device.patient` and any parameter named by a deployed scope as availability-critical. Making the status transition itself refuse to disable an authorization-relevant parameter (option 4) remains an attractive follow-up as defense in depth, and would convert most of these denials into a clear error at the point of the misconfiguration instead of at request time.
- One accepted trade-off in the read path: if `_id` is unavailable and the requested resource is not on the page the search returns, an authorized read can `404`. This only occurs in a configuration that is already broken, and it fails closed.
- **SQL only.** Cosmos does not implement the Device restriction, so no equivalent gap exists there and it is deliberately untouched. The scope-constraint and read-identity invariants are provider-agnostic and apply to both.
- This ADR complements rather than supersedes [ADR-2607](adr-2607-smart-include-compartment-scoping.md). That ADR established *what* is in a compartment and that the union and the include predicate share one rule source; this one establishes what happens when the inputs to those rules are unavailable. The shared rule source is what allowed the fix to be made in one place and take effect on both paths.

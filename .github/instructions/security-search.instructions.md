---
applyTo: "src/**/Features/Search/**,src/**/Features/Compartment/**,src/**/Features/Definition/**,src/**/Features/Schema/**,src/**/Data/**/compartment.json,src/**/Data/**/*search-parameters.json,src/**/Features/Queries/**,src/**/Features/Storage/**,src/**/Features/Operations/Everything/**,src/**/Features/Operations/MemberMatch/**,src/**/Features/Resources/Get/**,src/**/Extensions/SearchServiceExtensions.cs,src/**/Features/KnownQueryParameterNames.cs"
excludeAgent: "cloud-agent"
---

# FHIR Security Review: Search

Search is where SMART scope and compartment restrictions become query predicates. A restriction that is added to the primary expression can still be lost when the query is rewritten, expanded, paged, or executed by a different path.

## Execution paths

1. **Alternate paths must keep the filter.** A new stored procedure, query hint, range scan, cache, semantic search, or provider branch that a caller's request can reach must return the same rows the ordinary expression path would allow. Flag any path that runs from `SearchOptions` without the SMART and compartment expressions.
2. **Internal parameters stay internal.** `SearchOptions.QueryHints` and the surrogate-ID range parameters select an internal path and are only valid when `IsBackgroundTask` is true. A new entry in `KnownQueryParameterNames` must be classed as public or internal. `SearchOptionsFactory` must reject an internal one from an external caller, not ignore it or hide it from the unsupported-parameter list.
3. **SQL Server and Cosmos DB must agree.** A change to `ExpressionAccessControl`, `SearchOptionsFactory`, `SqlQueryGenerator`, `QueryBuilder`, or `FhirCosmosSearchService` that enforces a restriction needs the matching change, or a stated reason, in the other provider.

## Expansion and rewriting

4. **Expanded results need the same restrictions as primary results.** For `_include`, `_revinclude`, `$includes`, chaining, `_has`, `$everything`, and counts, check both the resource type (`IncludeExpression.AllowedResourceTypesByScope`) and the compartment of every returned resource.
5. **A wildcard scope still needs the compartment filter.** Flag a branch that skips include or revinclude filtering because the scope contains `KnownResourceTypes.All`.
6. **Shared resource types are a pivot.** `SmartCompartmentSearchRewriter` lets every SMART caller read some types that sit outside the patient compartment. Adding a type to that set widens every caller's access. A search anchored on one of those types must not return another patient's data through `_revinclude` or chaining.
7. **New expressions and rewriters.** A new `Expression` type, rewriter, or visitor must be handled by `ExpressionAccessControl` and by both query generators. A visitor default that passes over an unknown node can drop a restriction.
8. **References.** Untyped, relative, versioned, and absolute references must resolve to a local resource of the expected type before they count as compartment membership. Check the query after rewriting, not the parsed input.

## Paging, history, and definitions

9. **Continuation tokens are caller input.** `ct` and `includesCt` can be crafted. Each page must apply the current caller's restrictions again. A token must not carry or select authorization.
10. **History and version reads.** Authorize the versions that are returned. A check on the current version does not cover a historical one.
11. **Definitions and indexes are policy inputs.** Compartment definitions, search parameter status, and reference indexes decide what a SMART caller can see. When one is disabled, stale, or mid-reindex, a search for a SMART-restricted caller must fail closed. Unrestricted callers are not affected.
12. **Lossy matching.** Truncated, case-folded, or approximate index values are candidates. Where the predicate enforces authorization, the final decision must use the exact value. Normalization that only affects ordinary search matching is not a finding.
13. **Existence leaks.** Totals, `next` links, and distinct errors or status codes must not reveal resources the caller cannot read.

## Tests to ask for

- Two patients, with a caller scoped to one. Assert the body, the count, and the links, not only the status code.
- The ordinary path and the changed path, including a second page.
- The provider and FHIR versions the change affects.

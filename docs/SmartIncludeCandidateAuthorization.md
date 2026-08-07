# Alternate Design: Candidate-Driven SMART Compartment Authorization for Includes

**Status:** Proposed
**Date:** 2026-07-10
**Related ADR:** [ADR-2607: Enforce SMART Patient Compartment Scoping on `_include` / `_revinclude` (SQL)](arch/adr-2607-smart-include-compartment-scoping.md)
**Scope:** SQL provider

**Generated SQL comparison:** [Repeated compartment union versus candidate authorization](flow%20diagrams/smart-include-candidate-authorization-comparison.excalidraw)

## Summary

Instead of regenerating the complete SMART patient-compartment union for an
`_include`, `_revinclude`, or `$includes` query, authorize only the resources
produced by each include branch.

The proposed query is driven by candidate resource keys. For each candidate,
SQL performs a semi-join against `ReferenceSearchParam` to determine whether
that specific resource belongs to the caller's patient compartment.

This avoids:

- Materializing all authorized resource keys. A patient compartment can contain
  millions of resources, so a temporary table containing the complete
  compartment is not practical.
- Regenerating the full compartment union in the second include statement.
- Carrying mutable SMART-compartment marker state through the general Core
  expression hierarchy.
- Synchronizing duplicated local and field state in `SqlQueryGenerator`.

The primary matched-resource search can continue using the existing compartment
union initially. This proposal changes only authorization of resources produced
by include expansion.

## Problem With the Current Approach

The primary search and include expansion are emitted as separate SQL statements:

1. The primary query creates the SMART compartment union and stores the matched
   page in `@FilteredData`.
2. The include query starts a new `;WITH` statement.
3. Because a CTE cannot cross the statement boundary, the SMART compartment
   union is generated again.
4. Every `_include` and `_revinclude` branch checks its produced resource
   against the regenerated union.

This has several costs:

- The generated SQL duplicates the compartment restrictions.
- SQL Server may evaluate the underlying union branches more than once because
  CTEs are not guaranteed to be materialized.
- Wildcard includes can expand the union across many resource and search
  parameter combinations.
- Large generated unions and predicates increase compilation time and memory.
- The implementation requires `_smartCompartmentUnionCTE`,
  `_smartCompartmentUnionVisited`, saved counters, and
  `Expression.IsSmartCompartmentUnionExpression`.

Materializing the complete authorized resource-key set in a temporary table was
considered and tested. It is not viable because the result can contain millions
of keys.

## Goals

- Prevent `_include`, `_revinclude`, and `$includes` from returning resources
  outside the current SMART patient compartment.
- Preserve existing include paging and continuation-token semantics.
- Avoid enumerating or materializing the full patient compartment.
- Keep authorization in SQL rather than filtering after resource retrieval.
- Support SMART V1 and V2, including fine-grained scope intersections.
- Support explicit and wildcard include expressions.
- Use formal FHIR compartment membership rules rather than treating every
  reference targeting Patient as compartment membership.
- Remove the need to regenerate the SMART compartment union for includes.

## Non-Goals

- Changing Cosmos DB behavior.
- Adding paging support for iterative includes. The current implementation
  returns a truncation warning instead of a `$includes` link for iterative
  includes.
- Reintroducing the deprecated `CompartmentAssignment` table.
- Materializing a per-request table containing all authorized resource keys.
- Changing the existing `$includes` continuation-token format.

## Core Design

### Materialize Rules, Not Resource Keys

The implementation needs a compact description of patient-compartment
membership:

- Compartment root resource type, such as Patient.
- Compartment root resource ID.
- Resource types that are always allowed as shared include targets.
- For each patient-compartment resource type, the materialized search parameter
  IDs that establish membership.

Conceptually:

```text
SmartCompartmentMembership
  RootResourceTypeId
  RootResourceId
  SharedResourceTypeIds
  MembershipParameters
    ResourceTypeId -> SearchParamIds
```

`MembershipParameters` should contain only the formal FHIR compartment
parameters and explicitly validated materialized equivalents. It must not
include every search parameter whose target list contains Patient.

For combined parameters such as `clinical-patient` that are not materialized,
the mapping should list the specific materialized parameters that implement the
same compartment relationship, such as the applicable `subject` or `patient`
parameter.

The mapping is small. It can be represented as:

- An inline `VALUES` relation.
- A small table-valued parameter.
- A small table variable with a primary key.
- A persistent metadata table populated from the model.

Only the membership rules are stored. Resource keys are never precomputed.

### Immutable `SmartCompartmentMembershipContext`

The implementation represents those rules with an internal SQL-layer record:

```text
SmartCompartmentMembershipContext
  CompartmentResourceType
  CompartmentResourceId
  ImmutableArray<SharedResourceTypes>
  ImmutableArray<SmartCompartmentMembershipRule>

SmartCompartmentMembershipRule
  ResourceType
  ImmutableArray<SearchParameterUrls>
```

The context contains query metadata, not query results:

| Field | Purpose |
|---|---|
| `CompartmentResourceType` | The compartment root type, currently normally `Patient`. |
| `CompartmentResourceId` | The authorized root resource ID from the SMART request context. |
| `SharedResourceTypes` | Resource types that retain the existing universal/shared behavior, such as Practitioner and Organization. |
| `MembershipRules` | Formal compartment membership parameters grouped by candidate resource type. |

Search parameter URLs are stored instead of SQL `SearchParamId` values. The SQL
model resolves each URL to the database-specific numeric ID when generating the
query. This keeps the context independent of schema/model ID assignment and
allows the same structure to work across supported FHIR versions.

Both the context and its rules are sealed records backed by `ImmutableArray`.
They are constructed once for a search and cannot be mutated while expression
visitors or the SQL generator process the query. The context is query-scoped; it
is not a global cache and it does not survive between requests.

#### Construction

`SmartCompartmentMembershipContextFactory` searches the pre-SQL Core expression
tree for the request's `SmartCompartmentSearchExpression`. It then:

1. Reads the compartment type and authorized resource ID.
2. Gets formal resource-type and parameter relationships from the FHIR
   `CompartmentDefinition`.
3. Resolves each relationship to supported, materialized reference search
   parameters.
4. Applies only explicitly configured materialized equivalents for combined
   parameters that are not directly indexed.
5. Copies the shared resource-type list from
   `SmartCompartmentSearchRewriter.UniversalResourceTypes`.
6. Sorts resource types and parameter URLs before creating immutable arrays, so
   generated SQL remains deterministic.

The current explicit equivalent mapping covers every compartment-definition
parameter whose FHIRPath expression filters a polymorphic reference with
`resolve()` (such parameters are never materialized as `ReferenceSearchParam`
rows, because `resolve()` cannot be evaluated during indexing):

```text
# Patient compartment
clinical-patient      -> patient, subject
AuditEvent-patient    -> agent, entity
Basic-patient         -> subject
Invoice-patient       -> subject
MeasureReport-patient -> subject
Person-patient        -> link
Provenance-patient    -> target

# Practitioner compartment (SMART user launch)
Encounter-practitioner -> participant
Person-practitioner    -> link
```

Each candidate equivalent must exist for that resource type, be a supported
reference parameter, and be capable of targeting the compartment root type. If
no equivalent exists and the formal parameter itself is materialized, the
formal parameter is retained.

Known gap: `EpisodeOfCare-care-manager` (Practitioner compartment) is
`resolve()`-based and has **no** materialized equivalent — `EpisodeOfCare` has
no other reference parameter over `careManager`. The formal parameter is
retained so the resource type still yields a rule, but it matches nothing until
the parameter becomes indexable. This mirrors the pre-existing behavior of the
primary compartment search and is not a regression.

This is deliberately narrower than looking for every parameter that can target
Patient. For example, `Observation.focus` targets Patient but is not nominated
by the Patient `CompartmentDefinition`, so it is not added to the context.

#### Attachment and Lifetime

The SQL service creates the context from the original Core expression after the
normal SQL expression rewriting has completed:

```text
Core search expression containing SmartCompartmentSearchExpression
  -> SQL expression rewrites
  -> SqlRootExpression
  -> attach immutable SmartCompartmentMembershipContext
  -> SQL generation
```

It is attached for both:

- The initial search path after `IncludeRewriter`.
- The follow-up `$includes` path after `IncludesOperationRewriter`.

Attaching it to `SqlRootExpression` keeps SQL-specific authorization metadata
out of the base Core `Expression` hierarchy. It also removes the need for
generic visitors to copy a mutable boolean marker while rebuilding expression
nodes.

#### SQL Consumption

`SqlQueryGenerator` reads the context only while generating an include branch.
For each produced candidate it emits three alternatives:

1. The candidate is the compartment root resource and its ID matches.
2. The candidate type is in `SharedResourceTypes`.
3. A `ReferenceSearchParam` row exists for the candidate key and matches one of
   the formal `MembershipRules`, the compartment root type, and the compartment
   root ID.

The third alternative is keyed by the candidate's
`(ResourceTypeId, ResourceSurrogateId)` and uses `EXISTS`, so membership rows do
not multiply the candidate. The predicate is emitted inside the include
branch's `WHERE` clause before its `TOP`, which preserves `$includes` paging.

### Candidate Definition

The resource requiring authorization differs by include direction:

- `_include`: authorize the target resource reached through the reference.
- `_revinclude`: authorize the source resource containing the reference.

Both are already available in `HandleTableKindInclude` as a resource type ID and
resource surrogate ID.

### Candidate Membership Predicate

The general predicate is:

```sql
AND
(
    -- The compartment root itself.
    (
        Candidate.ResourceTypeId = @PatientResourceTypeId
        AND Candidate.ResourceId = @PatientId
    )

    -- Explicitly shared resource types.
    OR Candidate.ResourceTypeId IN (@SharedResourceTypeIds)

    -- Patient-compartment resource membership.
    OR EXISTS
    (
        SELECT 1
        FROM dbo.ReferenceSearchParam AS Membership
        JOIN @AllowedCompartmentParams AS Allowed
          ON Allowed.ResourceTypeId = Membership.ResourceTypeId
         AND Allowed.SearchParamId = Membership.SearchParamId
        WHERE Membership.ResourceTypeId = Candidate.ResourceTypeId
          AND Membership.ResourceSurrogateId = Candidate.ResourceSurrogateId
          AND Membership.ReferenceResourceTypeId = @PatientResourceTypeId
          AND Membership.ReferenceResourceId = @PatientId
          AND Membership.BaseUri IS NULL
    )
)
```

The exact shared-resource policy must match the existing SMART behavior. It
should be explicit rather than represented by synthetic universal members in a
large union.

`EXISTS` is required instead of a normal join so multiple qualifying reference
rows cannot multiply the included resource.

## Index Usage

`ReferenceSearchParam` currently has this clustered index:

```text
(ResourceTypeId, ResourceSurrogateId, SearchParamId)
```

The candidate-driven predicate supplies the first two values from the candidate
resource. SQL Server therefore performs a narrow seek over the reference rows
belonging to that resource and checks the small allowed search-parameter set.

This is fundamentally different from driving the query from
`ReferenceResourceId = @PatientId`, which can identify millions of resources in
the compartment.

Expected access patterns:

- `_include`: find outgoing references from the bounded matched page, resolve
  the target resource, then seek its membership rows by target resource key.
- `_revinclude`: use the target-reference index to find resources referring to
  the matched page, then seek each source resource's membership rows by source
  resource key.

The existing filtered-statistics support for `ReferenceSearchParam` can remain
as a supplementary optimization, but correctness must not depend on it.

## Integration With `SqlQueryGenerator`

The candidate membership predicate should replace the regenerated-compartment
CTE checks currently emitted in `HandleTableKindInclude`.

The implementation point is important: authorization must be added to the
include branch's `WHERE` clause before the branch-level `TOP`.

The primary query may continue using the existing SMART compartment union. The
include statement no longer needs to:

- Save and restore a compartment union table counter.
- Save a compartment union expression and query generator.
- Regenerate the compartment CTE set.
- Record `_smartCompartmentUnionCTE`.
- Discover the union through
  `Expression.IsSmartCompartmentUnionExpression`.

An immutable membership descriptor should instead be attached to
`SqlRootExpression` or another SQL-layer query context by the SQL rewriter. This
keeps SQL-specific state out of the base Core `Expression` class and ensures
that expression reconstruction cannot silently discard the marker.

## Initial Search and `$includes` Flow

The current paging model can be preserved.

### Initial Search

1. Execute the matched-resource search under the SMART compartment restriction.
2. Store the matched page in `@FilteredData`.
3. Execute each include branch.
4. Apply candidate authorization within each branch.
5. If the include result exceeds `_includesCount`, return an includes
   continuation token.
6. `BundleFactory` emits a link with relation `related` targeting
   `/{resourceType}/$includes`.

The related link retains the original query and adds `includesCt`.

### Following the Related Link

1. `IncludesController` executes the request as an includes operation.
2. `SearchOptionsFactory` decodes `includesCt` and recreates the original
   search, include expressions, SMART scopes, and compartment context.
3. `SearchIncludeImpl` restricts the matched resources to the surrogate-ID
   range stored in the token.
4. Every include branch applies the include-resource cursor.
5. Every include branch applies candidate authorization.
6. Authorized branch results are combined and globally limited.
7. The extra authorized resource becomes the boundary for the next link.
8. `BundleFactory` emits that link as the bundle's `next` link.

Authorization is re-evaluated on every page. A continuation token is not treated
as proof that a resource is still authorized.

## Paging Requirements

### Required Processing Order

Each include branch must use this order:

```text
Matched-resource range
  -> include continuation boundary
  -> candidate authorization
  -> DISTINCT candidate resource key
  -> ORDER BY resource type ID and surrogate ID
  -> TOP (_includesCount + 1)
```

After all branches:

```text
Authorized branch pages
  -> UNION ALL
  -> global DISTINCT
  -> global ORDER BY resource type ID and surrogate ID
  -> global TOP (_includesCount + 1)
```

The first `_includesCount` authorized resources are returned. The extra
authorized resource indicates that another page exists and supplies the
continuation boundary.

### Why Authorization Cannot Be Deferred

The current generator applies `TOP (_includesCount + 1)` inside every include
branch before the final include union.

This shape is incorrect:

```text
Branch TOP
  -> union branches
  -> authorize candidates
  -> final page
```

Unauthorized candidates could consume a branch's `TOP` allowance. Valid
resources later in the branch would never reach the authorization stage,
causing:

- Under-filled pages.
- Missing authorized resources.
- Incorrect partial-result detection.
- Continuation links that skip authorized resources.

Authorization must remain inside each branch before its existing `TOP`, unless
the query generator is redesigned to remove all branch limits and apply one
authorized global limit.

### Continuation Token

The current include cursor can remain:

```text
(IncludeResourceTypeId, IncludeResourceSurrogateId)
```

The next page applies a lexicographic boundary:

```sql
Candidate.ResourceTypeId > @LastResourceTypeId
OR
(
    Candidate.ResourceTypeId = @LastResourceTypeId
    AND Candidate.ResourceSurrogateId > @LastResourceSurrogateId
)
```

Because the boundary is selected from the authorized `N+1` result, the cursor
advances through the authorized resource set rather than the raw candidate set.

Unauthorized resources between two authorized resources are skipped by SQL and
do not consume page slots.

### Multiple Include Branches

The same global cursor is applied to every branch. Each branch returns at most
`N+1` authorized keys after that boundary. The final union deduplicates and
selects the next global page.

Authorization should remain an `EXISTS` predicate so the same resource matching
multiple compartment references does not appear multiple times.

### Authorization Changes Between Pages

The query rechecks membership on every `$includes` request:

- A resource that loses authorization is not returned on a later page.
- A resource newly authorized below the existing cursor might not appear in the
  current traversal.

This is consistent with the existing keyset-paging behavior when resources are
created, updated, or deleted between requests. Security revocation takes
priority over snapshot consistency.

### Iterative Includes

The current service does not generate `$includes` continuation links when
`ContainsIterativeInclude` is true. It returns a truncation warning instead.

Candidate-driven authorization should still be applied to iterative include
branches, but adding pageable iterative includes is a separate design.

## SMART V2 Fine-Grained Scopes

Candidate compartment authorization and SMART V2 scope authorization are
independent intersections.

An included resource must satisfy:

```text
Include relationship
AND SMART patient-compartment membership
AND SMART V2 resource/search-parameter scope
```

The initial implementation can keep the existing SMART V2 scope-union handling
and replace only the patient-compartment union used by includes.

A later follow-up could apply the same candidate-driven strategy to SMART V2
scope unions if measurements show similar benefits.

## Security Requirements

- Membership rules must be based on `CompartmentDefinition`.
- A reference parameter is not a membership parameter merely because it can
  target Patient.
- `Observation.focus` must not establish Patient compartment membership.
- Custom Patient-targeting search parameters must not automatically change
  compartment membership.
- Shared-resource behavior must be explicitly defined.
- The compartment root resource must match the authorized compartment ID.
- The `$includes` token must never bypass authorization. Following a related
  link with another caller's token must reapply the second caller's compartment
  and scopes.

## Performance Characteristics

### Expected Benefits

- Work scales primarily with include candidates rather than total compartment
  size.
- No per-request table containing millions of authorized keys.
- No second copy of the complete compartment union.
- Smaller generated SQL and reduced optimizer compilation work.
- Candidate membership uses the clustered `ReferenceSearchParam` access path.
- Explicit include types allow static partition elimination.

### Remaining Worst Case

For `_revinclude`, a matched resource can have a very large number of incoming
references. If most candidates are unauthorized, SQL may examine many
candidates before finding `N+1` authorized results.

This does not require materializing the patient compartment, but it must be
measured. SQL Server should be allowed to choose between:

- Candidate-driven nested-loop semi-joins.
- Joining the inbound-reference and membership relations before the page limit.

Hard-coded join hints should not be introduced without production-scale plan
evidence.

## Implementation Outline

1. Define an immutable SQL-layer SMART compartment membership descriptor.
2. Build the descriptor from the FHIR `CompartmentDefinition` and the verified
   materialized search-parameter mappings.
3. Preserve the descriptor through SQL root rewrites.
4. Add a query-generator helper that emits candidate membership predicates.
5. Use that helper in every `_include` and `_revinclude` branch before `TOP`.
6. Stop regenerating the SMART compartment union in the include statement.
7. Remove the include-specific compartment union fields, local snapshots, and
   expression marker when no longer required.
8. Keep the primary-query compartment union unchanged for the first iteration.
9. Capture actual execution plans and compare CPU, reads, duration, compilation
   time, memory grants, and spills.

## Required Tests

### Authorization

- `_include` cannot return a resource belonging to another patient.
- `_revinclude` cannot return a resource belonging to another patient.
- `Observation.subject = Patient/B` and `Observation.focus = Patient/A` is not
  visible to Patient A.
- The same Observation is not returned by
  `_revinclude=Observation:focus`.
- A supported custom Patient-targeting parameter does not establish membership.
- Valid materialized mappings for Encounter, Condition, Procedure, and
  ImagingStudy remain accessible.
- Shared Practitioner, Organization, Location, Medication, and Device behavior
  remains unchanged.

### Paging

- Unauthorized candidates occur before the first authorized candidate.
- Unauthorized candidates are interleaved between authorized candidates.
- The first `N+1` raw candidates in a branch are unauthorized.
- Multiple include and revinclude branches use `_includesCount=1`.
- A page boundary crosses from one resource type to another.
- Wildcard `_include=*:*` and `_revinclude=*:*` return every authorized resource
  exactly once across all pages.
- Following a related link with a different Patient token does not disclose the
  original patient's resources.
- Sorted searches preserve the existing two-phase includes continuation token.
- No related link is introduced for iterative includes.

### Performance

- Patient compartments containing millions of resources.
- `_include` from a normal matched page.
- `_revinclude` with high inbound-reference fan-out.
- Multiple explicit include branches.
- Wildcard include and revinclude.
- Dense authorized candidates and sparse authorized candidates.
- Current implementation versus candidate-driven implementation using actual
  execution plans.

## Acceptance Criteria

- No out-of-compartment resource is returned on an initial include page or any
  `$includes` continuation page.
- Authorized resources are neither skipped nor duplicated across pages.
- The existing includes continuation-token format remains compatible.
- Unauthorized candidates do not consume returned page slots.
- No complete patient-compartment key set is materialized.
- Candidate membership uses an index seek by resource type and surrogate ID in
  representative plans.
- Query performance remains acceptable for compartments containing millions of
  resources and for high-fan-out reverse includes.

## Tradeoffs

- The SQL generator gains a dedicated candidate-membership predicate.
- A compact membership-rule mapping must be maintained accurately across FHIR
  versions.
- Shared-resource policy becomes explicit and testable.
- The design performs one membership probe per candidate unless SQL Server
  chooses a set-based semi-join.
- The primary query and include query temporarily use different physical
  implementations of the same logical compartment rule. Both must be generated
  from the same membership descriptor to prevent semantic drift.

## Open Questions

- Should the compact `(ResourceTypeId, SearchParamId)` mapping be emitted as
  inline `VALUES`, passed as a table-valued parameter, or stored as model
  metadata in SQL?
- Which resource types should be treated as universally shared include targets?
- Should the primary SMART compartment query eventually adopt the same
  candidate-driven membership descriptor?
- Is the existing `OPTION (RECOMPILE)` still beneficial after the large union is
  removed?
- Do wildcard reverse includes need additional plan-shaping for extremely sparse
  authorized candidates?

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
- **The regular (non-SMART) compartment search path is deliberately left untouched.** The additive enumeration above is gated to `SmartCompartmentSearchExpression`; a conventional `Patient/{id}/*` compartment search still builds membership only from the `CompartmentDefinition`-named parameters and keeps its existing behavior — including the same pre-existing under-match for types mapped to the non-materialized combined parameter. This scoping keeps the security fix confined to the SMART path and guarantees it cannot regress non-SMART compartment semantics; broadening the base path is a separate, non-security change that was intentionally not bundled here.
- **Known residual gap (an under-match, never a disclosure): types whose *only* Patient link is the combined parameter are still not admitted.** `Immunization` is the concrete example. In R4 its sole `Patient`-targeting reference parameter is the combined `patient` parameter (`clinical-patient`); it has no type-specific materialized parameter (its other reference parameters — `location`, `manufacturer`, `performer`, `reaction`, `reason-reference` — do not target `Patient`, and there is no `Immunization-subject`). Because the enumeration only adds *supported, materialized* reference parameters and `clinical-patient` is neither, an `Immunization` linked only through it remains absent from the compartment result (e.g. `Immunization/A1` in the tests). This is a pre-existing completeness gap — the missing rows reference the compartment root itself, so it can never surface another patient's data — and closing it (by materializing the combined parameter or adding an equivalent indexed path) is out of scope for this security fix and left as a follow-up.
- Adds an `EXISTS` sub-predicate and a re-generated compartment-union CTE to include/revinclude statements. For targeted queries the cost is bounded (a semi-join over a small produced-row set against the `ReferenceSearchParam` index); a reversed wildcard `_revinclude=*:*` is the worst case, where the all-types compartment union dominates the text and is emitted twice. The generated SQL and its performance characteristics — captured before and after the fix — are analyzed in [Generated SQL and Performance Impact](#generated-sql-and-performance-impact).
- Include/revinclude of genuinely shared, non-patient-specific reference targets (e.g. `Practitioner`, `Organization`, `Location`, `Medication`) must remain available; these are represented in the compartment union's universal-type members so they are not dropped.
- **SQL-only.** Cosmos is deliberately untouched (deprecated). If Cosmos SMART includes need the same guarantee later, it is a separate follow-up.

## Generated SQL and Performance Impact

A concern raised in review: intersecting the compartment union with includes re-generates CTEs and could enlarge the query, hurting query-plan compilation and execution. To ground this rather than reason about it abstractly, the actual parameterized SQL (`sp_executesql` statement text) emitted by the R4 integration suite against SQL Server 2022 was captured **before** and **after** the fix, for the same requests.

### Representative case — a single `_include`

Request: `GET Coverage?_id=smart-leak-coverage&_include=Coverage:subscriber`, caller confined to `Patient/smart-leak-child` (`@p0 = 'smart-leak-child'`, `@p1 = 'smart-leak-coverage'`).

The query is two statements: a primary statement that materializes the compartment-restricted matches into `@FilteredData`, then an include statement that expands `_include`. The compartment union is `cte0` (rows in `ReferenceSearchParam` that reference the patient) `UNION ALL` `cte1` (the patient's own row) → `cte2`. Two things change with the fix:

1. **Primary `cte0` widens** from 4 to 7 reference-parameter branches (`SearchParamId` 343/345/336/342 → additionally 341/1015/1132), from the additive materialized-parameter enumeration (Fix #1 in the Decision). Same CTE count, a longer `OR` predicate.
2. **The include statement gains the compartment intersection.** Before, the include CTE (`cte6`) resolved `Coverage:subscriber` with *no* compartment check — the leak. After, the compartment union (`cte0`/`cte1`/`cte2`) is re-generated inside the include statement and `cte6` gains a second `EXISTS`, so the included target must also be in the compartment membership set.

The critical difference is one predicate on `cte6` (the include-target fetch):

```sql
-- BEFORE (leak): the included target only has to be referenced by a matched row (cte5)
,cte6 AS (
    SELECT DISTINCT TOP (@p3) refTarget.ResourceTypeId AS T1, refTarget.ResourceSurrogateId AS Sid1, 0 AS IsMatch, 0 AS IsPartial
    FROM dbo.ReferenceSearchParam refSource
         JOIN dbo.Resource refTarget ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId
    WHERE refSource.SearchParamId = 345
        AND refTarget.IsHistory = 0 AND refTarget.IsDeleted = 0
        AND refSource.ResourceTypeId IN (31)
        AND EXISTS (SELECT * FROM cte5 WHERE refSource.ResourceTypeId = T1 AND refSource.ResourceSurrogateId = Sid1 AND Row < @p4)
)

-- AFTER (fixed): the included target must ALSO be in the compartment membership CTE (cte2)
,cte6 AS (
    SELECT DISTINCT TOP (@p3) refTarget.ResourceTypeId AS T1, refTarget.ResourceSurrogateId AS Sid1, 0 AS IsMatch, 0 AS IsPartial
    FROM dbo.ReferenceSearchParam refSource
         JOIN dbo.Resource refTarget ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId
    WHERE refSource.SearchParamId = 345
        AND refTarget.IsHistory = 0 AND refTarget.IsDeleted = 0
        AND refSource.ResourceTypeId IN (31)
        AND EXISTS (SELECT * FROM cte5 WHERE refSource.ResourceTypeId = T1 AND refSource.ResourceSurrogateId = Sid1 AND Row < @p4)
        AND EXISTS (SELECT * FROM cte2 WHERE refSource.ReferenceResourceTypeId = T1 AND refTarget.ResourceSurrogateId = Sid1)  -- compartment intersection (the fix)
)
```

| Metric | Before | After | Δ |
|---|---|---|---|
| Statement text | 104 lines / ~3.9 KB | 170 lines / ~6.1 KB | +66 lines / +63 % |
| CTEs (primary ; include) | 6 ; 4 | 6 ; 7 | +3 (compartment union re-generated in the include) |
| Compartment predicate on include | none | one `EXISTS` semi-join | the fix |
| Primary `INSERT` option | — | `OPTION (RECOMPILE)` | see below |

<details>
<summary>Full generated SQL — BEFORE (leak)</summary>

```sql
DECLARE @FilteredData AS TABLE (T1 smallint, Sid1 bigint, IsMatch bit, IsPartial bit, Row int)
;WITH
cte0 AS
(
    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1
    FROM dbo.ReferenceSearchParam
    WHERE ((SearchParamId = 343
        AND ResourceTypeId = 31
        AND ((ReferenceResourceTypeId = 100)
        OR (ReferenceResourceTypeId = 103)
        OR (ReferenceResourceTypeId = 114)
        OR (ReferenceResourceTypeId IS NULL))  )
        OR (SearchParamId = 345
        AND ResourceTypeId = 31
        AND ((ReferenceResourceTypeId = 103)
        OR (ReferenceResourceTypeId = 114)
        OR (ReferenceResourceTypeId IS NULL))  )
        OR (SearchParamId = 336
        AND ResourceTypeId = 31
        AND ReferenceResourceTypeId = 103 )
        OR (SearchParamId = 342
        AND ResourceTypeId = 31
        AND ((ReferenceResourceTypeId = 100)
        OR (ReferenceResourceTypeId = 103)
        OR (ReferenceResourceTypeId = 114)
        OR (ReferenceResourceTypeId IS NULL))  )) 
        AND ReferenceResourceTypeId = 103
        AND ReferenceResourceId = @p0 
),
cte1 AS
(
    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1
    FROM dbo.Resource
    WHERE IsHistory = 0 
        AND IsDeleted = 0 
        AND ResourceId = @p0
        AND ResourceTypeId = 103 
),
cte2 AS
(
    SELECT * FROM cte0
    UNION ALL SELECT * FROM cte1
), 
cte3 AS
(
    SELECT T1, Sid1, ResourceTypeId AS T2, ResourceSurrogateId AS Sid2
    FROM dbo.Resource
         JOIN cte2 ON ResourceTypeId = T1 AND ResourceSurrogateId = Sid1
    WHERE IsHistory = 0 
        AND ResourceTypeId = 31 
)
,cte4 AS
(
    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1
    FROM dbo.Resource
         JOIN cte3 ON ResourceTypeId = cte3.T1 AND ResourceSurrogateId = cte3.Sid1
    WHERE IsHistory = 0 
        AND IsDeleted = 0 
        AND ResourceTypeId = 31
        AND ResourceId = @p1 
)
,cte5 AS
(
    SELECT row_number() OVER (ORDER BY T1 ASC, Sid1 ASC) AS Row, *
    FROM
    (
        SELECT DISTINCT TOP (@p2) T1, Sid1, 1 AS IsMatch, 0 AS IsPartial 
        FROM cte4
        ORDER BY T1 ASC, Sid1 ASC
    ) t
)
/* HASH 8RoAjbAA9k8Qql6sbY5yVKEt4QG93ilwLWK5XdzxemY= params=@p0 */
INSERT INTO @FilteredData SELECT T1, Sid1, IsMatch, IsPartial, Row FROM cte5
;WITH cte5 AS (SELECT * FROM @FilteredData)
,cte6 AS
(
    SELECT DISTINCT TOP (@p3) refTarget.ResourceTypeId AS T1, refTarget.ResourceSurrogateId AS Sid1, 0 AS IsMatch, 0 AS IsPartial 
    FROM dbo.ReferenceSearchParam refSource
         JOIN dbo.Resource refTarget ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId
    WHERE refSource.SearchParamId = 345
        AND refTarget.IsHistory = 0 
        AND refTarget.IsDeleted = 0 
        AND refSource.ResourceTypeId IN (31)
        AND EXISTS (SELECT * FROM cte5 WHERE refSource.ResourceTypeId = T1 AND refSource.ResourceSurrogateId = Sid1 AND Row < @p4)
)
,cte7 AS
(
    SELECT DISTINCT TOP (@p5) T1, Sid1, IsMatch, CASE WHEN count_big(*) over() > @p6 THEN 1 ELSE 0 END AS IsPartial 
    FROM cte6
)
,cte8 AS
(
    SELECT T1, Sid1, IsMatch, IsPartial 
    FROM cte5
    UNION ALL
    SELECT T1, Sid1, IsMatch, IsPartial
    FROM cte7 WHERE NOT EXISTS (SELECT * FROM cte5 WHERE cte5.Sid1 = cte7.Sid1 AND cte5.T1 = cte7.T1)
)
SELECT * FROM (SELECT DISTINCT r.ResourceTypeId, r.ResourceId, r.Version, r.IsDeleted, r.ResourceSurrogateId, r.RequestMethod, CAST(IsMatch AS bit) AS IsMatch, CAST(IsPartial AS bit) AS IsPartial, r.IsRawResourceMetaSet, r.SearchParamHash, r.RawResource
FROM dbo.Resource r
     JOIN cte8 ON r.ResourceTypeId = cte8.T1 AND r.ResourceSurrogateId = cte8.Sid1
WHERE IsHistory = 0 
    AND IsDeleted = 0 
) AS t ORDER BY IsMatch DESC, (CASE WHEN IsMatch = 1 THEN t.ResourceTypeId ELSE NULL END) ASC, (CASE WHEN IsMatch = 1 THEN t.ResourceSurrogateId ELSE NULL END) ASC, (CASE WHEN IsMatch = 0 THEN t.ResourceTypeId ELSE NULL END) ASC, (CASE WHEN IsMatch = 0 THEN t.ResourceSurrogateId ELSE NULL END) ASC
```

</details>

<details>
<summary>Full generated SQL — AFTER (fixed)</summary>

```sql
DECLARE @FilteredData AS TABLE (T1 smallint, Sid1 bigint, IsMatch bit, IsPartial bit, Row int)
;WITH
cte0 AS
(
    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1
    FROM dbo.ReferenceSearchParam
    WHERE ((SearchParamId = 343
        AND ResourceTypeId = 31
        AND ((ReferenceResourceTypeId = 100)
        OR (ReferenceResourceTypeId = 103)
        OR (ReferenceResourceTypeId = 114)
        OR (ReferenceResourceTypeId IS NULL))  )
        OR (SearchParamId = 345
        AND ResourceTypeId = 31
        AND ((ReferenceResourceTypeId = 103)
        OR (ReferenceResourceTypeId = 114)
        OR (ReferenceResourceTypeId IS NULL))  )
        OR (SearchParamId = 336
        AND ResourceTypeId = 31
        AND ReferenceResourceTypeId = 103 )
        OR (SearchParamId = 342
        AND ResourceTypeId = 31
        AND ((ReferenceResourceTypeId = 100)
        OR (ReferenceResourceTypeId = 103)
        OR (ReferenceResourceTypeId = 114)
        OR (ReferenceResourceTypeId IS NULL))  )
        OR (SearchParamId = 341
        AND ResourceTypeId = 31
        AND ReferenceResourceTypeId = 103 )
        OR (SearchParamId = 1015
        AND ResourceTypeId = 103
        AND ((ReferenceResourceTypeId = 103)
        OR (ReferenceResourceTypeId = 114)
        OR (ReferenceResourceTypeId IS NULL))  )
        OR (SearchParamId = 1132
        AND ResourceTypeId = 114
        AND ReferenceResourceTypeId = 103 )) 
        AND ReferenceResourceTypeId = 103
        AND ReferenceResourceId = @p0 
),
cte1 AS
(
    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1
    FROM dbo.Resource
    WHERE IsHistory = 0 
        AND IsDeleted = 0 
        AND ResourceId = @p0
        AND ResourceTypeId = 103 
),
cte2 AS
(
    SELECT * FROM cte0
    UNION ALL SELECT * FROM cte1
), 
cte3 AS
(
    SELECT T1, Sid1, ResourceTypeId AS T2, ResourceSurrogateId AS Sid2
    FROM dbo.Resource
         JOIN cte2 ON ResourceTypeId = T1 AND ResourceSurrogateId = Sid1
    WHERE IsHistory = 0 
        AND ResourceTypeId = 31 
)
,cte4 AS
(
    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1
    FROM dbo.Resource
         JOIN cte3 ON ResourceTypeId = cte3.T1 AND ResourceSurrogateId = cte3.Sid1
    WHERE IsHistory = 0 
        AND IsDeleted = 0 
        AND ResourceTypeId = 31
        AND ResourceId = @p1 
)
,cte5 AS
(
    SELECT row_number() OVER (ORDER BY T1 ASC, Sid1 ASC) AS Row, *
    FROM
    (
        SELECT DISTINCT TOP (@p2) T1, Sid1, 1 AS IsMatch, 0 AS IsPartial 
        FROM cte4
        ORDER BY T1 ASC, Sid1 ASC
    ) t
)
/* HASH 8RoAjbAA9k8Qql6sbY5yVKEt4QG93ilwLWK5XdzxemY= params=@p0 */
INSERT INTO @FilteredData SELECT T1, Sid1, IsMatch, IsPartial, Row FROM cte5
OPTION (RECOMPILE)
;WITH
cte0 AS
(
    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1
    FROM dbo.ReferenceSearchParam
    WHERE ((SearchParamId = 343
        AND ResourceTypeId = 31
        AND ((ReferenceResourceTypeId = 100)
        OR (ReferenceResourceTypeId = 103)
        OR (ReferenceResourceTypeId = 114)
        OR (ReferenceResourceTypeId IS NULL))  )
        OR (SearchParamId = 345
        AND ResourceTypeId = 31
        AND ((ReferenceResourceTypeId = 103)
        OR (ReferenceResourceTypeId = 114)
        OR (ReferenceResourceTypeId IS NULL))  )
        OR (SearchParamId = 336
        AND ResourceTypeId = 31
        AND ReferenceResourceTypeId = 103 )
        OR (SearchParamId = 342
        AND ResourceTypeId = 31
        AND ((ReferenceResourceTypeId = 100)
        OR (ReferenceResourceTypeId = 103)
        OR (ReferenceResourceTypeId = 114)
        OR (ReferenceResourceTypeId IS NULL))  )
        OR (SearchParamId = 341
        AND ResourceTypeId = 31
        AND ReferenceResourceTypeId = 103 )
        OR (SearchParamId = 1015
        AND ResourceTypeId = 103
        AND ((ReferenceResourceTypeId = 103)
        OR (ReferenceResourceTypeId = 114)
        OR (ReferenceResourceTypeId IS NULL))  )
        OR (SearchParamId = 1132
        AND ResourceTypeId = 114
        AND ReferenceResourceTypeId = 103 )) 
        AND ReferenceResourceTypeId = 103
        AND ReferenceResourceId = @p0 
),
cte1 AS
(
    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1
    FROM dbo.Resource
    WHERE IsHistory = 0 
        AND IsDeleted = 0 
        AND ResourceId = @p0
        AND ResourceTypeId = 103 
),
cte2 AS
(
    SELECT * FROM cte0
    UNION ALL SELECT * FROM cte1
)
,cte5 AS (SELECT * FROM @FilteredData)
,cte6 AS
(
    SELECT DISTINCT TOP (@p3) refTarget.ResourceTypeId AS T1, refTarget.ResourceSurrogateId AS Sid1, 0 AS IsMatch, 0 AS IsPartial 
    FROM dbo.ReferenceSearchParam refSource
         JOIN dbo.Resource refTarget ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId
    WHERE refSource.SearchParamId = 345
        AND refTarget.IsHistory = 0 
        AND refTarget.IsDeleted = 0 
        AND refSource.ResourceTypeId IN (31)
        AND EXISTS (SELECT * FROM cte5 WHERE refSource.ResourceTypeId = T1 AND refSource.ResourceSurrogateId = Sid1 AND Row < @p4)
        AND EXISTS (SELECT * FROM cte2 WHERE refSource.ReferenceResourceTypeId = T1 AND refTarget.ResourceSurrogateId = Sid1)
)
,cte7 AS
(
    SELECT DISTINCT TOP (@p5) T1, Sid1, IsMatch, CASE WHEN count_big(*) over() > @p6 THEN 1 ELSE 0 END AS IsPartial 
    FROM cte6
)
,cte8 AS
(
    SELECT T1, Sid1, IsMatch, IsPartial 
    FROM cte5
    UNION ALL
    SELECT T1, Sid1, IsMatch, IsPartial
    FROM cte7 WHERE NOT EXISTS (SELECT * FROM cte5 WHERE cte5.Sid1 = cte7.Sid1 AND cte5.T1 = cte7.T1)
)
SELECT * FROM (SELECT DISTINCT r.ResourceTypeId, r.ResourceId, r.Version, r.IsDeleted, r.ResourceSurrogateId, r.RequestMethod, CAST(IsMatch AS bit) AS IsMatch, CAST(IsPartial AS bit) AS IsPartial, r.IsRawResourceMetaSet, r.SearchParamHash, r.RawResource
FROM dbo.Resource r
     JOIN cte8 ON r.ResourceTypeId = cte8.T1 AND r.ResourceSurrogateId = cte8.Sid1
WHERE IsHistory = 0 
    AND IsDeleted = 0 
) AS t ORDER BY IsMatch DESC, (CASE WHEN IsMatch = 1 THEN t.ResourceTypeId ELSE NULL END) ASC, (CASE WHEN IsMatch = 1 THEN t.ResourceSurrogateId ELSE NULL END) ASC, (CASE WHEN IsMatch = 0 THEN t.ResourceTypeId ELSE NULL END) ASC, (CASE WHEN IsMatch = 0 THEN t.ResourceSurrogateId ELSE NULL END) ASC
```

</details>

### Worst case — reversed wildcard `_revinclude=*:*`

Request: `GET Patient?_id=smart-patient-D&_revinclude=*:*`. There is no bounded list of types that may reference the patient, so the compartment union is emitted over **all** compartment resource types — a single giant `OR` predicate ~4,350 lines long — and, like every include, that union is re-generated once more for the revinclude statement.

| Metric | Before | After |
|---|---|---|
| Statement text | 89 lines / ~3.5 KB | 8,814 lines / ~360 KB |
| Compartment union `cte0` | small (primary types only) | ~4,350 lines, emitted **twice** |
| `UNION ALL` operators | few | 5 (the bulk is one wide `OR`, not many unions) |

This is the fix's genuine worst case and the sharpest form of the reviewer's concern: the all-types compartment union dominates the text and is duplicated across the two statements. It is the price of enforcing the compartment on every revincluded type. A targeted `_revinclude=Type:param` stays close to the single-`_include` numbers above.

### Why this is acceptable — and where it is not

- **Plan compilation & caching.** The include re-generation reuses the *pre-existing* SMART V2 scope-union pattern, which appends `OPTION (RECOMPILE)` to the `INSERT INTO @FilteredData` (`SqlQueryGenerator`, gated on `_smartV2UnionVisited || _smartCompartmentUnionVisited`). Consequence: the statement is compiled per execution — the optimizer sees the actual literals and the real cardinality of the `@FilteredData` table variable — and, importantly, the large wildcard plan is **not** cached, so it can neither bloat nor evict the plan cache for other workloads. The compartment id stays a parameter (`@p0`) and the predicate is metadata-derived (compartment definition + search-parameter ids), so it is identical across patients and independent of stored data.
- **Execution.** Compartment membership is applied as a correlated `EXISTS (SELECT * FROM <unionCTE> …)` — a semi-join keyed on `(ResourceTypeId, ResourceSurrogateId)`, the leading columns of the `Resource`/`ReferenceSearchParam` indexes — evaluated only over the produced include rows, which are themselves capped by `TOP (@p3)` (≈1,001). The union CTE is built from `ReferenceSearchParam` index seeks (`SearchParamId = … AND ReferenceResourceId = @p0`). For targeted queries this is a handful of seeks over a bounded row set, not a scan.
- **Text size / duplication.** The compartment union CTE is emitted twice (primary + include). For targeted SMART queries that is tens of lines; for `_revinclude=*:*` it is thousands. SQL Server parses even the large text quickly, but the wildcard text size and per-execution recompilation are real, non-zero costs.
- **Net.** For the SMART queries a patient-scoped app actually issues — a resource, or a compartment slice, with targeted includes — the added cost is a bounded semi-join plus a modest predicate, and the plan-cache behavior of non-include queries is unchanged (with no includes the generated SQL is byte-for-byte identical to before). The unbounded `_revinclude=*:*` case is the outlier and a natural follow-up optimization: emit the compartment union **once** (e.g. materialize it into a table variable / temp table and reference it from both statements) rather than re-generating it, and/or cap wildcard-revinclude type expansion. Neither is required to close the disclosure, so both are deferred to keep this change focused on the security fix.

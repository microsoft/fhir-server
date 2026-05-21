# Scalar Temporal Equality Rewriter for Birthdate

## Context

FHIR date equality on a single-element parameter like `Patient.birthdate` is currently a two-predicate containment filter: `DateTimeStart >= periodStart AND DateTimeEnd <= periodEnd`. Against `dbo.DateTimeSearchParam` this shape forces the optimizer to reason over the full row set for that `SearchParamId`, even though, in practice, almost every stored birthdate row is a single calendar day (`IsLongerThanADay = 0`).

The table carries a filtered index intended for the rare wide rows:
`IX_SearchParamId_StartDateTime_EndDateTime_INCLUDE_IsMin_IsMax_WHERE_IsLongerThanADay_1`. For `IsLongerThanADay = 0` there is no filtered index — the common short rows are served by the general `IX_SearchParamId_StartDateTime_EndDateTime_INCLUDE_IsLongerThanADay_IsMin_IsMax. The previous SQL generated creates a two-predicate form which doesn't build SQL the planner can use to target either index. This means it picks a generic seek on the clustered index which is far less efficient than using the non-clustered indexes on our indexes we havn.

The FHIR containment semantics for partial-date equality are separately tracked by AB#191826 and intentionally left untouched here.

## Decision

We will add a new Date search param rewriter scoped to **exact UTC calendar-day equality on allow-listed scalar date parameters (currently `individual-birthdate`)** and emit a `UnionExpression` split on `IsLongerThanADay`:

- **Short branch** (`IsLongerThanADay = 0 AND DateTimeEnd = endOfDay`): a point-equality on `EndDateTime` that the `EndDateTime`-leading general index can satisfy as a narrow seek; `IsLongerThanADay = 0` becomes a residual on the dominant population.
- **Long branch** (`IsLongerThanADay = 1 AND DateTimeStart >= startOfDay AND DateTimeEnd <= endOfDay`): the old logic can be run on queries that a more than a day and take advantage of the special index maded for this: `IX_SearchParamId_StartDateTime_EndDateTime_INCLUDE_IsMin_IsMax_WHERE_IsLongerThanADay_1`, which only stores the wide rows and is orders of magnitude smaller than the general index.

We will observe this change and add more search parameters in the future as needed.

Part of this work was confirming that the equality is valid - all current FHIR SQL rows have the EndDateTime stored in a way compatibly with this query. Details shared outside of the ADR.

All other shapes — month/year precision, approximate (`ap`), single-sided range operators, non-UTC values, composites, and non-allow-listed parameters — pass through unchanged. We could add a minor optimization for year only queries but in practice the actual SQL execution plan was not very different in performance (time or resources used).

There is also discussion on if the data returned by this query is matching the FHIR spec. In this change we are only keeping current behavior. If we change our date range return values in the future we can simply remove the second (union) part of the rewrite query easily.

## Status

 Accepted

## Consequences

- Day-precision birthdate equality consistently picks the intended filtered/specific indexes per branch instead of a generic plan over the mixed population, reducing logical reads on `dbo.DateTimeSearchParam` for the dominant workload.
- The long branch becomes dead once AB#191826 lands; at that point the union collapses to the short branch and this ADR should be revisited.
- The allow-list (currently `individual-birthdate`) limits blast radius. Extending the optimization to additional scalar date parameters is a follow-up that requires confirming the same single-element storage assumption.

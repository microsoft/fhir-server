# Patient Birthdate Partial-Date Query Design

## Problem

The current SQL date-only optimization improves exact-day birthdate queries such as `birthdate=2016-03-03` by rewriting the normal FHIR equality containment predicates into a single `EndDateTime` equality. That rewrite is intentionally limited to one calendar day and does not help partial FHIR dates such as `birthdate=2000` or `birthdate=2000-03`.

FHIR `date` values may be partial. This repository already stores those values as search ranges:

- `2000` is indexed as `StartDateTime = 2000-01-01T00:00:00Z` and `EndDateTime = 2000-12-31T23:59:59.9999999Z`.
- `2000-03` is indexed as `StartDateTime = 2000-03-01T00:00:00Z` and `EndDateTime = 2000-03-31T23:59:59.9999999Z`.
- `2000-03-03` is indexed as `StartDateTime = 2000-03-03T00:00:00Z` and `EndDateTime = 2000-03-03T23:59:59.9999999Z`.

Because year and month values are longer than a day, their `DateTimeSearchParam.IsLongerThanADay` value is true.

## Goals

- Improve SQL query generation for partial-date `Patient.birthdate` equality searches.
- Preserve the existing FHIR equality semantics implemented by Core: a resource date range matches when it is fully contained by the query date range.
- Keep the change scoped to Patient birthdate behavior rather than all date-only search parameters.
- Preserve the existing exact-day optimization and approximate-date safeguards.

## Non-goals

- Do not change indexing or database schema.
- Do not change Core search expression generation.
- Do not broaden this optimization to every `IsDateOnly` search parameter.
- Do not accidentally optimize non-Patient uses of the shared `individual-birthdate` search parameter unless implementation confirms the SQL expression context cannot distinguish those resource types and the same containment proof holds for them.
- Do not optimize composite date search parameters in this change.

## Current Behavior

Indexing converts FHIR date values through `DateToDateTimeSearchValueConverter` and `DateTimeSearchValue`. Missing date components are expanded to the earliest possible `Start` and latest possible `End` for the specified precision. SQL writes these values into `dbo.DateTimeSearchParam`.

For date equality, Core currently emits:

```sql
StartDateTime >= @queryStart
AND EndDateTime <= @queryEnd
```

For `birthdate=2000`, that shape is semantically correct, but it does not give SQL Server the same simple `EndDateTime`-first access path as the exact-day optimization.

## Proposed Design

Add a birthdate-specific partial-date rewrite in the SQL expression rewrite layer, adjacent to the existing `DateOnlyEqualityRewriter` and before `DateTimeEqualityRewriter`.

The rewriter should recognize this shape:

```sql
StartDateTime >= @queryStart
AND EndDateTime <= @queryEnd
```

When all of these are true:

- The search parameter is the standard birthdate parameter: `Code == "birthdate"` and `Url == "http://hl7.org/fhir/SearchParameter/individual-birthdate"`.
- The expression context is a Patient search. If the visitor cannot prove Patient context, implementation should either move the rewrite to a layer that has resource-type context or keep the guard limited to the standard birthdate parameter only after confirming the same date-only containment proof applies to all resources sharing that parameter.
- The parameter is date-only.
- The query range is longer than one calendar day and matches a precise FHIR partial-date boundary: a full calendar month or a full calendar year.
- Both constants are `DateTimeOffset` values.
- The value shape is not an approximate (`ap`) expansion.

Rewrite the expression to:

```sql
EndDateTime >= @queryStart
AND EndDateTime <= @queryEnd
```

For FHIR `date`-precision birthdate values, this keeps containment semantics:

- `birthdate=2000` matches stored `2000`, `2000-03`, and `2000-03-03`.
- `birthdate=2000-03` matches stored `2000-03` and dates in March 2000.
- `birthdate=2000-03` does not match stored `2000`, because that year-level value is broader than the query range.
- Neighboring years and months remain excluded by the `EndDateTime` lower and upper bounds.

## Alternatives Considered

1. Keep exact-day-only behavior. This is lowest risk but does not improve year-only or month-only birthdate searches.
2. Add generic precision metadata and rewrite all date-only parameters by query precision. This is more extensible, but it increases scope and is unnecessary for the current birthdate-only goal.
3. Recommended: add a targeted birthdate partial-date rewrite using the existing `EndDateTime` index path. This keeps scope small while addressing the partial-date case directly.

## Safeguards

- Exact one-day queries should continue using the existing `EndDateTime == endOfDay` rewrite.
- Approximate (`ap`) queries should pass through unchanged. They produce a similar two-predicate shape, but the constants are expanded around the query range and do not represent stored birthdate precision boundaries.
- Non-birthdate date parameters should pass through unchanged.
- Composite parameters should pass through unchanged.
- If the rewriter cannot prove the expected value shape, it should leave the original expression unchanged.

## Testing

Add unit tests for:

- Year-only `birthdate=2000` rewriting to an `EndDateTime` range.
- Month-only `birthdate=2000-03` rewriting to an `EndDateTime` range.
- Exact-day `birthdate=2000-03-03` preserving the existing single-column equality rewrite.
- Approximate birthdate queries passing through unchanged.
- Date-only non-birthdate parameters passing through unchanged.
- Non-date-only birthdate-like parameters passing through unchanged.

Add SQL E2E coverage when implementing:

- Patients with `birthDate` values `2000`, `2000-03`, `2000-03-03`, adjacent years, and adjacent months.
- Verify `birthdate=2000` returns the contained year/month/day values.
- Verify `birthdate=2000-03` returns March values but not the broader `2000` value.
- Verify `birthdate=2000-03-03` retains the existing exact-day result behavior.

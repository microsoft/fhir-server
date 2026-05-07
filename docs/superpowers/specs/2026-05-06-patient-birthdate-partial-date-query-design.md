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
- Add reusable metadata that identifies date/dateTime search parameters whose indexed values come from scalar temporal elements rather than explicit range elements.
- Activate the new SQL rewrite only for a known allow-list initially containing the standard birthdate parameter.
- Preserve the existing exact-day optimization and approximate-date safeguards.

## Non-goals

- Do not change indexing or database schema.
- Do not change Core search expression generation.
- Do not broaden this optimization to every `IsDateOnly` search parameter.
- Do not activate the rewrite for every scalar temporal parameter in the first implementation.
- Do not optimize composite date search parameters in this change.

## Current Behavior

Indexing converts FHIR date values through `DateToDateTimeSearchValueConverter` and `DateTimeSearchValue`. Missing date components are expanded to the earliest possible `Start` and latest possible `End` for the specified precision. SQL writes these values into `dbo.DateTimeSearchParam`.

For date equality, Core currently emits:

```sql
StartDateTime >= @queryStart
AND EndDateTime <= @queryEnd
```

For `birthdate=2000`, that shape is semantically correct, but it does not give SQL Server the same simple `EndDateTime`-first access path as the exact-day optimization.

The existing generic `DateTimeEqualityRewriter` runs later and is designed for the broader `date`, `dateTime`, `instant`, `Period`, and `Timing` world. The partial-date birthdate optimization must run before that generic rewrite so it can preserve the scalar date containment proof and avoid explicit range cases.

## Proposed Design

Add reusable scalar-temporal metadata and a guarded SQL equality rewrite. The metadata can identify broader safe candidates, but the rewrite is activated only for a small canonical allow-list in v1.

### 1. Metadata: scalar temporal search parameters

Add a derived flag to `SearchParameterInfo`, named `IsScalarTemporal`, computed by `SearchParameterSupportResolver` alongside the existing support metadata.

`IsScalarTemporal` is true when all of these are true:

- The search parameter type is `SearchParamType.Date`.
- The parameter is not composite.
- Every type-resolution result for the expression has a scalar temporal FHIR node type: `date`, `dateTime`, or `instant`.
- No type-resolution result is an explicit range-capable temporal type such as `Period` or `Timing`.

This flag is broader than `IsDateOnly`. `Patient.birthDate` is both date-only and scalar temporal. A parameter such as `Observation.effective[x]` is not scalar temporal if its expression can resolve to `Period` or `Timing`.

The flag is derived metadata and should not change search parameter hashing or stored search index rows.

### 2. Activation allow-list

Add a SQL rewrite allow-list keyed by canonical search parameter URL. The initial allow-list contains only:

```text
http://hl7.org/fhir/SearchParameter/individual-birthdate
```

The standard `individual-birthdate` parameter is shared by Patient, Person, and RelatedPerson, but all of those fields are scalar FHIR `date` values. The allow-list still prevents the new rewrite from applying to arbitrary scalar temporal parameters until they are individually reviewed and added.

### 3. SQL expression rewrite

Add the partial-date equality rewrite in the SQL expression rewrite layer, adjacent to the existing `DateOnlyEqualityRewriter` and before `DateTimeEqualityRewriter`.

The rewriter should recognize this shape:

```sql
StartDateTime >= @queryStart
AND EndDateTime <= @queryEnd
```

When all of these are true:

- The search parameter has `IsScalarTemporal == true`.
- The search parameter URL is in the activation allow-list.
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

The existing exact-day birthdate rewrite can remain as the more selective fast path:

```sql
EndDateTime = @queryEnd
```

For future allow-listed scalar `dateTime` or `instant` parameters, exact-day or exact-time behavior should be reviewed separately because those values may end at points inside the query interval rather than always at `endOfDay`.

## Alternatives Considered

1. Keep exact-day-only behavior. This is lowest risk but does not improve year-only or month-only birthdate searches.
2. Add scalar-temporal metadata and immediately rewrite every eligible parameter. This is more extensible, but it activates too much behavior at once.
3. Recommended: add scalar-temporal metadata now, but activate the rewrite only through a small allow-list initially containing birthdate. This gives the implementation a reusable safety model while limiting runtime behavior change.

## Safeguards

- Exact one-day queries should continue using the existing `EndDateTime == endOfDay` rewrite.
- Approximate (`ap`) queries should pass through unchanged. They produce a similar two-predicate shape, but the constants are expanded around the query range and do not represent stored birthdate precision boundaries.
- Parameters not in the activation allow-list should pass through unchanged, even if `IsScalarTemporal == true`.
- Parameters resolving to `Period`, `Timing`, or any other explicit range-capable temporal shape should not be marked `IsScalarTemporal`.
- Composite parameters should pass through unchanged.
- If the rewriter cannot prove the expected value shape, it should leave the original expression unchanged.

## Testing

Add unit tests for:

- `SearchParameterSupportResolver` marking scalar `date`, `dateTime`, and `instant` parameters as scalar temporal.
- `SearchParameterSupportResolver` excluding `Period`, `Timing`, mixed scalar/range parameters, and composite parameters from scalar temporal metadata.
- Allow-list behavior: birthdate is active; an otherwise scalar temporal parameter not on the allow-list passes through unchanged.
- Year-only `birthdate=2000` rewriting to an `EndDateTime` range.
- Month-only `birthdate=2000-03` rewriting to an `EndDateTime` range.
- Exact-day `birthdate=2000-03-03` preserving the existing single-column equality rewrite.
- Approximate birthdate queries passing through unchanged.
- Explicit range-capable date parameters passing through unchanged.

Add SQL E2E coverage when implementing:

- Patients with `birthDate` values `2000`, `2000-03`, `2000-03-03`, adjacent years, and adjacent months.
- Verify `birthdate=2000` returns the contained year/month/day values.
- Verify `birthdate=2000-03` returns March values but not the broader `2000` value.
- Verify `birthdate=2000-03-03` retains the existing exact-day result behavior.

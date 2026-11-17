# SQL equality optimization for calendar dates

**Status**: Proposed revision of the accepted containment-only decision.

## Context

FHIR [`date`](https://hl7.org/fhir/R4/datatypes.html#date) values represent a calendar year, month, or day. The SQL index stores each as an interval. Default equality uses overlap, so `birthdate=2021-01-15` also matches stored values `2021-01` and `2021`. `FhirSqlServer:EnableFhirDateContainment` selects [containment](https://hl7.org/fhir/R4/search.html#prefix) instead, matching only stored intervals inside the query interval.

We want a more selective comparison for day queries without changing the configured results or the surrounding query structure.

## Options considered

- Require containment. This would change existing customers' results.
- Compare only end boundaries for every value. This would lose overlap matches with stored months and years.
- Split the query into a temporal union. This adds query structure that complicates chained searches.
- Rewrite the date predicate in place. This preserves semantics without adding query-level branches.

## Decision

Use `ScalarTemporalEqualityRewriter` for built-in, non-composite Date search parameters whose derived `TemporalType.Date` metadata proves every path extracts a FHIR `date`. Definitions combining only date paths qualify. Patient birthdate and ClaimResponse payment-date are examples, not an allowlist.

Only equality queries spanning exactly one full UTC calendar day qualify. Custom definitions and other or unproven temporal types keep their existing paths, as do year and month queries, timestamps, other operators, and unsupported expression shapes.

Among valid FHIR dates, only day values have `IsLongerThanADay = false`. They match a full query day under either equality rule when their end boundaries are equal. Under overlap, a disjoint OR branch retains the original range comparison for stored months and years. The downstream equality rewriter converts that range to overlap. Under containment, a month or year cannot fit inside one day, so the long-value branch is unnecessary. No extra table expression or SQL-generator special case is introduced.

## Consequences

The optimization is SQL-only and uses the existing index schema. It requires no migration, data rewrite, or reindex. It assumes valid built-in date values and does not repair malformed historical data or parameter overrides.

The predicate allows index-friendly plans, not a guaranteed seek or speedup. Workloads with many partial dates still need range comparisons. See [SQL search architecture](../SqlSearchArchitecture.md#calendar-date-equality) for implementation details.

Enable both `FhirSqlServer:EnableScalarTemporalEqualityRewriter` and `FhirServer:CoreFeatures:EnableSearchParameterTemporalClassification`, then restart because classification runs during built-in definition initialization. Without classification, scalar enablement logs a startup warning and retains existing SQL. Both settings default to false. The containment setting and `FhirSqlServer:EnableInstantDateTimeRewriter` remain independent and also default to false.

Before enablement, validate representative results, paging, authorization, and query plans. Turning off scalar optimization restores the unoptimized path without changing the configured equality semantics.

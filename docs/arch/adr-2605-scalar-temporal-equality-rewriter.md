# Scalar temporal equality rewriter for built-in calendar dates

## Context

FHIR date `eq` on `dbo.DateTimeSearchParam` has two supported behaviors. Core emits **containment**, `DateTimeStart >= lo AND DateTimeEnd <= hi`, as specified by [FHIR R4 search prefixes](https://hl7.org/fhir/R4/search.html#prefix). The default `DateTimeEqualityRewriter` changes it to legacy **overlap**, `DateTimeStart <= hi AND DateTimeEnd >= lo`. Product alignment requires preserving overlap for existing customers, including matching stored month/year birthdates against a day query.

The earlier optimization split birthdate equality into a Core `UnionExpression`. PR #5569 introduced that query-level split. PRs #5607/#5608 and #5621 restricted it after regressions inside chains and beside sibling chains. The additional table/CTE structure, rather than the date predicate itself, coupled the optimization to SQL-generation rules.

This ADR initially avoided the split by requiring containment. The revision below removes that requirement without reintroducing a temporal union. Temporal classification from PR #6009 now proves which built-in definitions extract only FHIR `date` leaves, so eligibility no longer needs a DOB URL allowlist.

## Options considered

1. **Containment as a prerequisite**. Rejected for this optimization because changing customer-visible semantics is out of scope. The separate containment option remains available.
2. **End-only equality for every stored value**. Rejected because it drops matching month/year birthdates.
3. **A Core temporal union or additional SQL-stage branches**. Rejected because they change query structure. The measured predicate-level alternative does not require them.
4. **A predicate-level OR for exact-day equality**. Chosen. Keep the short-date seek and long-date overlap together inside one existing search-parameter expression.
5. **A new canonical-day schema discriminator**. Rejected as unnecessary for the built-in `date` contract. This is not a general optimization for arbitrary `dateTime` or `Period` values.

## Decision

Keep `EnableScalarTemporalEqualityRewriter` independent of `EnableFhirDateContainment`. Both defaults remain `false`. Scalar optimization also requires Core's default-off `EnableSearchParameterTemporalClassification`. With classification and scalar optimization enabled, run `ScalarTemporalEqualityRewriter` before applying the selected equality semantics.

| `EnableScalarTemporalEqualityRewriter` | `EnableFhirDateContainment` | `ScalarTemporalEqualityRewriter` | `DateTimeEqualityRewriter` (overlap) | eligible calendar-date `eq` | ineligible date `eq` | Core temporal union introduced |
|---|---|---|---|---|---|---|
| on | on | runs → End-only predicate | skipped | End-only seek (containment) | containment | none |
| off | on | does not run | skipped | Core containment | containment | none |
| on | off | runs, retaining long dates | runs on retained ranges | End-only day seek OR long-date overlap | legacy overlap | none introduced |
| off | off | does not run | runs | legacy overlap | legacy overlap | none |

For an eligible day query, the short branch is `IsLongerThanADay = 0 AND DateTimeEnd = endOfDay`. When containment is off, retain the original equality range under `IsLongerThanADay = 1` in a second OR branch. The existing equality rewriter converts that retained range to overlap. The branches are disjoint, and neither adds a Core `UnionExpression`, a table expression, or a generator special case.

The table assumes Core classification is enabled before definition initialization. Eligibility requires `IsSystemDefined`, search type `Date`, no components, and derived `TemporalType.Date`. Homogeneous date unions remain eligible, including built-in `individual-birthdate` expressions. `ClaimResponse.payment.date` qualifies in R4, R5, and STU3. `Basic.created` qualifies in R4 and STU3, but its R5 `dateTime` leaf does not.

The query must have one inclusive equality pair covering a full UTC calendar day. Query year/month precision, timestamps, other operators, and comma-OR expressions retain their existing paths. Repeated parameters and chained predicates can each be optimized independently when their individual expression has the eligible shape. The maximum representable day uses the existing overflow-safe end calculation.

The stored-value invariant is specific to [FHIR `date`](https://hl7.org/fhir/R4/datatypes.html#date). A valid date is a calendar year, month, or day. The date converter expands it to the corresponding UTC interval. Only a stored day has `IsLongerThanADay = 0`, so overlap with a query day is equivalent to equality of their end points. Month/year rows still use both endpoints. Custom definitions, composites, and `DateTime`, `Instant`, `Period`, `Timing`, `Mixed`, or `Unknown` classifications are ineligible, even if a custom definition carries `Date` metadata.

The real typed-JSON conversion rejects timestamps in FHIR `date` fields. The production HTTP parser's existing `TruncateDateTimeToDate` setting instead normalizes them to dates before indexing. Constructing an invalid POCO directly can bypass that boundary. It does not extend the supported calendar-date contract.

**The scalar optimization is SQL-only.** Core date semantics and the schema are unchanged. `ap` retains its existing widened overlap expression. Existing containment-on behavior is unchanged. Setting the scalar flag to `false` restores the previous date query path without changing the selected semantics.

Validation exposed an existing composition defect with the scalar flag both off and on. Shared expression rewrites now preserve SMART scope grouping metadata. This fix does not expand the central `SqlQueryGenerator` and remains active when the scalar optimization is disabled.

## Status

Proposed revision to the accepted containment-only decision, implemented behind a default-off flag. Customer-pattern validation is required before accepting or enabling the revised optimization.

## Consequences

- Day-query optimization no longer requires a change to overlap semantics. Stored month/year matches remain present.
- Predicate construction stays in the existing small rewriter. The service only selects the mode and sequencing.
- Chains, sorting, includes, and SMART unions retain their existing query structure. The scalar rewrite adds no temporal union or chain-skip rule.
- SQL Server can seek the general EndDateTime index for the short branch and a filtered long-date index for the fallback. This is an available plan, not a guarantee that every workload selects it.
- A large partial-precision population can still make the long branch expensive. Customer cardinality, parameterization, cached plans, and complete query shapes remain the performance acceptance criteria.
- Corrupt indexes or historical overrides of the built-in parameter are not repaired by this change. Customers with such data must re-establish the built-in date contract before opting in.

## Verification

| Scenario | Flags (scalar / containment) | `eq` semantics | Proven by |
|---|---|---|---|
| Legacy | off / off | overlap | `SqlServerSearchServiceTests` and baseline executions in `SqlCalendarDateSearchTests` |
| Overlap + scalar | on / off | unchanged overlap results | `SqlCalendarDateSearchTests`, using generated SQL against connection-local SQL Server tables |
| Containment | off / on | containment | `SqlServerSearchServiceTests.ApplyDateEqualitySemantics` containment case (containment survives, no overlap swap, no union) |
| Containment + scalar | on / on | containment, calendar-date End-only seek | `ScalarTemporalEqualityRewriterTests` (single predicate, no `UnionExpression`) |

`SqlCalendarDateSqlGenerationTests` loads production built-in definitions and drives the parser-to-SQL pipeline for R4, R5, and STU3. It covers ClaimResponse payment dates, homogeneous birthdate unions, non-date leaves, both required flags, and partial-precision/operator fallbacks without a database. Versioned `SearchParameterDefinitionBuilderTests` verifies the underlying built-in metadata.

`SqlCalendarDateSearchTests` executes generated SQL against connection-local tables and compares exact results with the optimization off and on. Its shared equality and command helpers cover Patient birthdates and ClaimResponse payment dates, including counts and keyset pages under overlap and containment. Both parameters cover leap days and minimum/maximum dates. The Patient graph regressions cover forward/reverse chains, sorting, includes/revincludes, repeated and comma-separated parameters, and scoped matches/includes. Partial-precision and timestamp query fallbacks retain identical SQL. Keyset tests exercise generated page predicates, not HTTP continuation-token encoding.

The scope cases include birthdate inside mixed date/token scope unions, resource-specific include grants, and scopes combined with forward chains. The generated-SQL tests do not establish authenticated HTTP authorization or continuation-token encoding.

`SqlCalendarDateStorageTests` covers the production query parser, `SearchOptionsFactory`, indexing, and SQL storage. ClaimResponse payment-date cases check day, month, and year queries under overlap and containment, with accurate and count-only totals. The Patient graph cases check mixed birthdate/token scope unions with reverse-include grants and birthdate/Organization conjunctions with accurate and count-only totals. Its class-scoped `CalendarDateStorageFixture` enables classification before definitions initialize, without changing defaults for other storage tests. Each case runs with scalar optimization disabled and enabled. Its injected request scopes do not substitute for authenticated HTTP validation.

A local diagnostic using the complete generated direct query over synthetic date rows demonstrated an EndDateTime equality seek and a separate filtered long-date seek. It does not establish customer latency or throughput. XML and bulk-import ingestion were not included in the stored-date probe.

## Rollout

The deployment defaults remain `FhirSqlServer:EnableScalarTemporalEqualityRewriter=false` and `FhirServer:CoreFeatures:EnableSearchParameterTemporalClassification=false`. Enable both for this optimization. Their environment variables are `FhirSqlServer__EnableScalarTemporalEqualityRewriter` and `FhirServer__CoreFeatures__EnableSearchParameterTemporalClassification`. Restart after configuration changes because classification runs during built-in definition initialization. Derived metadata is not serialized, and reloading an existing definition retains its instance and classification.

If scalar optimization is enabled without classification, a startup warning explains why searches retain existing SQL. An overlap-preserving rollout does not change `FhirSqlServer:EnableFhirDateContainment`. Disabling the scalar flag restores the previous date query path. No schema migration, reindex, or data rewrite is part of this change. The instant rewriter and temporal classifier are unchanged.

Enablement requires representative customer-shaped validation of exact result sets, sorted continuation pages, authenticated SMART scopes, and query plans at realistic cardinalities. The synthetic regression suites do not prove every inventoried customer shape or a customer performance improvement. Keep the flag disabled until those acceptance checks are complete.

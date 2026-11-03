# Scalar Temporal Equality Rewriter for Birthdate

## Context

FHIR date `eq` on `dbo.DateTimeSearchParam` has two supported behaviors. Core emits **containment**, `DateTimeStart >= lo AND DateTimeEnd <= hi`, as specified by [FHIR R4 search prefixes](https://hl7.org/fhir/R4/search.html#prefix). The default `DateTimeEqualityRewriter` changes it to legacy **overlap**, `DateTimeStart <= hi AND DateTimeEnd >= lo`. Product alignment requires preserving overlap for existing customers, including matching stored month/year birthdates against a day query.

The earlier optimization split birthdate equality into a Core `UnionExpression`. PR #5569 introduced that query-level split. PRs #5607/#5608 and #5621 restricted it after regressions inside chains and beside sibling chains. The additional table/CTE structure, rather than the date predicate itself, coupled the optimization to SQL-generation rules.

This ADR initially avoided the split by requiring containment. The revision below removes that requirement without reintroducing a temporal union.

## Options Considered

1. **Containment as a prerequisite**. Rejected for this optimization because changing customer-visible semantics is out of scope. The separate containment option remains available.
2. **End-only equality for every stored value**. Rejected because it drops matching month/year birthdates.
3. **A Core temporal union or additional SQL-stage branches**. Rejected because they change query structure. The measured predicate-level alternative does not require them.
4. **A predicate-level OR for exact-day equality**. Chosen. Keep the short-date seek and long-date overlap together inside one existing search-parameter expression.
5. **A new canonical-day schema discriminator**. Rejected as unnecessary for the built-in `date` contract. This is not a general optimization for arbitrary `dateTime` or `Period` values.

## Decision

Make **`EnableScalarTemporalEqualityRewriter`** independent of **`EnableFhirDateContainment`**. Both defaults remain `false`. With scalar optimization enabled, run `ScalarTemporalEqualityRewriter` before applying the selected equality semantics.

| `EnableScalarTemporalEqualityRewriter` | `EnableFhirDateContainment` | `ScalarTemporalEqualityRewriter` | `DateTimeEqualityRewriter` (overlap) | birthdate `eq` | other date `eq` | Core temporal union introduced |
|---|---|---|---|---|---|---|
| on | on | runs → End-only predicate | skipped | End-only seek (containment) | containment | none |
| off | on | does not run | skipped | Core containment | containment | none |
| on | off | runs, retaining long dates | runs on retained ranges | End-only day seek OR long-date overlap | legacy overlap | none introduced |
| off | off | does not run | runs | legacy overlap | legacy overlap | none |

For an eligible day query, the short branch is `IsLongerThanADay = 0 AND DateTimeEnd = endOfDay`. When containment is off, retain the original equality range under `IsLongerThanADay = 1` in a second OR branch. The existing equality rewriter converts that retained range to overlap. The branches are disjoint, and neither adds a Core `UnionExpression`, a table expression, or a generator special case.

Eligibility remains the built-in `individual-birthdate` allow-list, a non-composite date parameter, and one inclusive equality pair covering a full UTC calendar day. Query year/month precision, timestamps, other operators, other parameters, and comma-OR expressions retain their existing paths. Repeated parameters and chained predicates can each be optimized independently when their individual expression has the eligible shape. The maximum representable day uses the existing overflow-safe end calculation.

The stored-value invariant is specific to [FHIR `date`](https://hl7.org/fhir/R4/datatypes.html#date). A valid birthdate is a calendar year, month, or day. The date converter expands it to the corresponding UTC interval. Only a stored day has `IsLongerThanADay = 0`, so overlap with a query day is equivalent to equality of their end points. Month/year rows still use both endpoints. An arbitrary short interval would invalidate this equivalence and must not be added to the allow-list.

The real typed-JSON conversion rejects timestamp-valued birthdates. The production HTTP parser's existing `TruncateDateTimeToDate` setting instead normalizes them to dates before indexing. Constructing an invalid POCO directly can bypass that boundary; it does not extend the supported birthdate contract.

**The scalar optimization is SQL-only.** Core date semantics and the schema are unchanged. `ap` retains its existing widened overlap expression. Existing containment-on behavior is unchanged. Setting the scalar flag to `false` restores the previous date query path without changing the selected semantics.

Validation exposed two existing composition defects with the scalar flag both off and on. Shared expression rewrites now preserve SMART scope grouping metadata. Chained `:missing=true` uses a target-correlated anti-existence predicate instead of treating the entire chain as a root-level absence test. These fixes do not expand the central `SqlQueryGenerator` and remain active when the scalar optimization is disabled.

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
| Legacy | off / off | overlap | `SqlServerSearchServiceTests` and baseline executions in `SqlBirthdateSearchTests` |
| Overlap + scalar | on / off | unchanged overlap results | `SqlBirthdateSearchTests`, using generated SQL against connection-local SQL Server tables |
| Containment | off / on | containment | `SqlServerSearchServiceTests.ApplyDateEqualitySemantics` containment case (containment survives, no overlap swap, no union) |
| Containment + scalar | on / on | containment, birthdate End-only seek | `ScalarTemporalEqualityRewriterTests` (single predicate, no `UnionExpression`) |

The generated-SQL suite compares exact results with the optimization off and on. It covers direct queries, forward/reverse and sibling chains, sorting, includes/revincludes, counts, keyset pages, repeated and comma-separated parameters, scoped matches/includes, leap days, and minimum/maximum dates. Partial-precision and timestamp query fallbacks retain identical SQL. Keyset tests exercise generated page predicates, not HTTP continuation-token encoding.

The scope cases include birthdate inside mixed date/token scope unions, resource-specific include grants, and scopes combined with forward and reverse chains. Negative controls distinguish a missing value on the linked resource from an absent link, including roots with both present and missing linked targets. The generated-SQL tests do not establish authenticated HTTP authorization or continuation-token encoding.

`SqlBirthdateStorageTests` provides permanent coverage through the production query parser, `SearchOptionsFactory`, indexing, and SQL storage. It checks reverse-chain missing predicates, mixed DOB/token scope unions with reverse-include grants, and DOB/Organization conjunctions with accurate and count-only totals. Each case runs with scalar optimization disabled and enabled, using the ordinary SQL storage fixture. Its injected request scopes do not substitute for authenticated HTTP validation.

A local diagnostic using the complete generated direct query over synthetic date rows demonstrated an EndDateTime equality seek and a separate filtered long-date seek. It does not establish customer latency or throughput. XML and bulk-import ingestion were not included in the stored-date probe.

## Rollout

The deployment default remains `FhirSqlServer:EnableScalarTemporalEqualityRewriter=false`. An overlap-preserving rollout enables that flag without changing `FhirSqlServer:EnableFhirDateContainment`. Disabling the scalar flag restores the previous date query path. No schema migration or data rewrite is part of this change.

Enablement requires representative customer-shaped validation of exact result sets, sorted continuation pages, authenticated SMART scopes, and query plans at realistic cardinalities. The synthetic regression suites do not prove every inventoried customer shape or a customer performance improvement. Keep the flag disabled until those acceptance checks are complete.

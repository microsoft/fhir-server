# ADR-2605: Scalable FHIR Search Control

**Status**: Proposed
**Date**: 2026-05-19
**Feature**: SQL query complexity scoring, telemetry, and BestEffort search controls

## Context

FHIR search allows callers to combine selective filters, `_include`, `_revinclude`, `_include:iterate`, sorting, `_total=accurate`, and large page sizes. Some of these requests are valid FHIR queries but have very different SQL execution risk than simple reads or selective searches. A deep include graph can generate dependent fan-out, plan instability, and intermittent long-running SQL subqueries even when the same route and resource type normally look healthy in aggregate latency metrics.

The service currently needs a way to distinguish performant searches from complex searches before SQL execution. Without that signal, operational metrics mix simple and expensive request shapes into the same bucket, outliers look like generic capacity problems, and future policies such as higher-SLA timeouts, reduced concurrency, BestEffort opt-in headers, or rejection thresholds have no deterministic input.

## Options Considered

1. **Continue relying on aggregate latency and timeout metrics** - Use existing operation/resource metrics only. *(rejected: they hide query-shape differences and cannot predict expensive requests before execution)*
2. **Optimize individual SQL plans as slow queries are found** - Tune indexes, hints, or query generation for specific bad plans. *(viable: still needed, but reactive and not sufficient for admission policy or normalized metrics)*
3. **Require callers to manually decompose deep include searches** - Have clients fetch the root search first and use `$includes` or follow-up searches for related resources. *(viable: lowers per-query SQL complexity, but requires client changes and does not help identify or govern complex requests centrally)*
4. **Automatically decompose and reassemble complex searches in the server** - Split deep include graphs into multiple internal searches and merge the bundle. *(deferred: promising, but hard to make correct across paging, ordering, authorization, consistency, duplicate handling, and continuation semantics)*
5. **Classify SQL search complexity before execution** - Score parsed search shape deterministically and emit structured telemetry. *(chosen)*

## Decision

We will add SQL-provider query complexity scoring during SQL search preparation. The score is deterministic and based on the parsed search shape, including resource constraints, search parameter types, `_total=accurate`, non-`_lastUpdated` sorts, large `_count` and `_includesCount`, `_include` and `_revinclude`, `_include:iterate`, wildcard includes, untyped references, include graph depth, and chain depth. The score maps to four tiers: `Standard`, `Complex`, `BestEffort`, and `Rejected`.

The first use is observability, not enforcement. Each SQL search records structured telemetry with the score, tier, correlation id, operation, method, route, resource type, include/count flags, and background-task flag. Client search responses also receive query complexity headers. This keeps the change reversible: thresholds can be tuned from production data before any blocking, timeout, or concurrency policy is attached.

If enforcement is enabled later, `BestEffort` queries should not be silently rejected by default. The caller should have an explicit opt-in path, such as `x-ms-fhir-expensive-query: best-effort`, which tells the service the caller accepts a higher-SLA or best-effort execution path. If the header is omitted, the service can return an OperationOutcome explaining why the query requires best-effort handling and how to narrow it or opt in. `Rejected` remains a separate tier for queries with very high synchronous timeout risk even if the caller is willing to opt in.

The score is SQL-specific for now because the cost factors reflect SQL search rewriting and SQL execution risk. The pattern can later become a provider-neutral request annotation if each data provider supplies its own calibrated calculator and maps to the same tier vocabulary.

## Scoring Algorithm

The calculator starts at zero, walks the parsed search expression tree, adds cost for known expensive shapes, then assigns the tier from the final score.

| Factor | Score impact |
| --- | ---: |
| No resource-type or compartment constraint | +50 |
| `_id` exact search | +1 |
| Token or reference search | +3 |
| Date, number, quantity, or URI search | +5 |
| Normal string or composite search | +10 |
| String `contains` or `ends-with`, or special search | +20 |
| Untyped reference search or include target | +25 |
| `_total=accurate` | +20 |
| Non-`_lastUpdated` sort | +25 |
| `_count > 100` | +10 |
| `_count >= 1000` | +30 additional |
| `_includesCount >= 1000` when includes are present | +30 |
| Each `_include` or `_revinclude` | +20 |
| Each `_include:iterate` or `_revinclude:iterate` | +40 additional |
| Wildcard include | +100 |
| Include graph depth | `depth^2 * 10` |
| Chain depth | `depth^2 * 15` |

The initial tiers are:

| Score | Tier | Intended meaning |
| ---: | --- | --- |
| 0-30 | `Standard` | Normal SLA, expected to be handled like ordinary selective searches. |
| 31-100 | `Complex` | Allowed, but should be measured separately from standard traffic. |
| 101-200 | `BestEffort` | Valid FHIR shape. If enforcement is enabled, require explicit caller opt-in such as `x-ms-fhir-expensive-query: best-effort`; otherwise return an explanatory OperationOutcome. |
| >200 | `Rejected` | Very high risk of synchronous timeout even with best-effort handling if enforcement is later enabled. Today this is telemetry only. |

Example scores:

| Query shape | Score calculation | Tier |
| --- | --- | --- |
| `GET /Patient?_id=abc` | `_id` exact search: 1 | `Standard` |
| `GET /Patient?name:contains=smith&_total=accurate&_sort=name` | string contains: 20, accurate total: 20, non-`_lastUpdated` sort: 25. Total: 65 | `Complex` |
| `GET /Patient?_include:iterate=*` | include: 20, iterate: 40, wildcard: 100, include depth 2: 40. Total: 200 | `BestEffort` |

Customer-style include graph example:

```http
GET /ServiceRequest?based-on=ServiceRequest/{id}
  &_count=1000
  &_revinclude=DiagnosticReport:based-on
  &_include:iterate=DiagnosticReport:result
  &_include:iterate=Observation:performer
  &_include:iterate=PractitionerRole:practitioner
  &_include:iterate=PractitionerRole:organization
```

This scores as `Rejected`: reference filter 3, `_count=1000` 40, `_includesCount=1000` 30, five includes 100, four iterative includes 160, and include depth 5 adds 250, for a total score of 583.

This also creates a metrics normalization boundary. Instead of comparing every `SearchByResourceType` request together, dashboards can split latency, timeout rate, SQL CPU, SQL IO, and request volume by route, resource type, complexity tier, and score band. If a caller decomposes one high-score request into a root search plus smaller include retrieval searches, telemetry will show several lower-score operations rather than one high-score operation, making the performance tradeoff measurable.

## Consequences

- SQL search metrics can be split by complexity tier and score bands, so P50/P95/P99 latency, timeout rate, CPU, IO, and request volume can be compared within normalized query-shape buckets instead of across all searches.
- Operational dashboards can separate normal service health from caller-selected expensive query shapes, reducing false capacity conclusions and making customer guidance more precise.
- Future admission control can use the same deterministic signal to require a BestEffort opt-in header such as `x-ms-fhir-expensive-query: best-effort`, route opted-in BestEffort searches to a higher SLA or lower-concurrency path, or reject shapes that exceed a configured threshold.
- Manual or future server-side decomposition can be evaluated with evidence: the same logical request can be compared as one high-score search versus several lower-score searches.
- The initial thresholds are heuristics and must be calibrated with production telemetry before enforcement.
- The scoring model adds maintenance cost: new search features, include behavior, or SQL query-generation changes must update the calculator and tests.

## References

- `docs/arch/adr-2503-Bundle-include-operation.md`
- HL7 FHIR R4 Search: `_include` and `_revinclude` semantics, https://hl7.org/fhir/R4/search.html#include

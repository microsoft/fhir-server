# ADR-2605: SQL Search Query Admission Control

**Status**: Proposed
**Date**: 2026-05-19
**Feature**: SQL query admission scoring and telemetry

## Context

FHIR search allows callers to combine selective filters, `_include`, `_revinclude`, `_include:iterate`, sorting, `_total=accurate`, and large page sizes. Some of these requests are valid FHIR queries but have very different SQL execution risk than simple reads or selective searches. A deep include graph can generate dependent fan-out, plan instability, and intermittent long-running SQL subqueries even when the same route and resource type normally look healthy in aggregate latency metrics.

The service currently needs a way to distinguish performant searches from complex searches before SQL execution. Without that signal, operational metrics mix simple and expensive request shapes into the same bucket, outliers look like generic capacity problems, and future policies such as higher-SLA timeouts, reduced concurrency, opt-in expensive-query headers, or rejection thresholds have no deterministic input.

## Options Considered

1. **Continue relying on aggregate latency and timeout metrics** - Use existing operation/resource metrics only. *(rejected: they hide query-shape differences and cannot predict expensive requests before execution)*
2. **Optimize individual SQL plans as slow queries are found** - Tune indexes, hints, or query generation for specific bad plans. *(viable: still needed, but reactive and not sufficient for admission policy or normalized metrics)*
3. **Require callers to manually decompose deep include searches** - Have clients fetch the root search first and use `$includes` or follow-up searches for related resources. *(viable: lowers per-query SQL complexity, but requires client changes and does not help identify or govern complex requests centrally)*
4. **Automatically decompose and reassemble complex searches in the server** - Split deep include graphs into multiple internal searches and merge the bundle. *(deferred: promising, but hard to make correct across paging, ordering, authorization, consistency, duplicate handling, and continuation semantics)*
5. **Classify SQL search complexity before execution** - Score parsed search shape deterministically and emit structured telemetry. *(chosen)*

## Decision

We will add SQL-provider query complexity scoring during SQL search preparation. The score is deterministic and based on the parsed search shape, including resource constraints, search parameter types, `_total=accurate`, non-`_lastUpdated` sorts, large `_count` and `_includesCount`, `_include` and `_revinclude`, `_include:iterate`, wildcard includes, untyped references, include graph depth, and chain depth. The score maps to four tiers: `Standard`, `Complex`, `Expensive`, and `Rejected`.

The first use is observability, not enforcement. Each SQL search records structured telemetry with the score, tier, correlation id, operation, method, route, resource type, include/count flags, and background-task flag. Client search responses also receive query complexity headers. This keeps the change reversible: thresholds can be tuned from production data before any blocking, timeout, or concurrency policy is attached.

The score is SQL-specific for now because the cost factors reflect SQL search rewriting and SQL execution risk. The pattern can later become a provider-neutral request annotation if each data provider supplies its own calibrated calculator and maps to the same tier vocabulary.

## Consequences

- SQL search metrics can be split by complexity tier and score bands, so P50/P95/P99 latency, timeout rate, CPU, IO, and request volume can be compared within normalized query-shape buckets instead of across all searches.
- Operational dashboards can separate normal service health from caller-selected expensive query shapes, reducing false capacity conclusions and making customer guidance more precise.
- Future admission control can use the same deterministic signal to require an expensive-query opt-in header, route complex searches to a higher SLA, lower concurrency for expensive searches, or reject shapes that exceed a configured threshold.
- Manual or future server-side decomposition can be evaluated with evidence: the same logical request can be compared as one high-score search versus several lower-score searches.
- The initial thresholds are heuristics and must be calibrated with production telemetry before enforcement.
- The scoring model adds maintenance cost: new search features, include behavior, or SQL query-generation changes must update the calculator and tests.

## References

- `docs/arch/adr-2503-Bundle-include-operation.md`
- HL7 FHIR R4 Search: `_include` and `_revinclude` semantics, https://hl7.org/fhir/R4/search.html#include

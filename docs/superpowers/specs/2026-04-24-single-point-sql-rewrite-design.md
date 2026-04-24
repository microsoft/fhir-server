# Single-Point Search Parameter SQL Rewrite Design

## Summary

Rework the current branch from `main` instead of extending the existing date-only implementation. The new design treats the optimization as a **policy-driven single-point rewrite** for specific search parameters, not as a new `SearchParamType` and not as a generic `date-only` feature.

For v1, the optimization is gated by a **code-owned allowlist** keyed by search parameter identity. Only allowlisted parameters and explicitly supported operators use the new one-column rewrite path. Everything else continues to use the existing generic two-column `(StartDateTime, EndDateTime)` SQL generation.

## Problem

The current branch solved a narrow case: `Patient.birthdate` equality queries where a date-only field is stored as a deterministic `(StartDateTime, EndDateTime)` pair. That improves one hotspot, but the underlying issue is broader:

- some search parameters are effectively stored as a **single comparable point** even though the SQL model uses two columns;
- the SQL generator currently treats those parameters the same as true range-like values;
- that prevents the optimizer from consistently choosing the best single-column seek plan.

The next design should target that broader shape without over-generalizing rollout or changing query semantics.

## Goals

- Start over from `main` and keep the new design independent from the current branch's date-only plumbing.
- Support a reusable **single-point behavior** that can be applied to specific search parameters.
- Optimize `eq`, `gt`, `ge`, `lt`, and `le` when a parameter is explicitly approved for that behavior.
- Keep the feature strictly performance-only: unsupported or unproven cases must fall back to the existing generic path.
- Keep the first rollout simple, auditable, and low-risk.

## Non-Goals

- No `SearchParamType` changes.
- No attempt to redesign all date or datetime SQL generation in v1.
- No support for `ap`, `sa`, `eb`, composite cases, or ambiguous operator semantics in v1.
- No runtime-configurable allowlist in v1.

## Design

### 1. Architectural boundary

The optimization is modeled as **single-point search behavior for selected parameters**.

- The **policy layer** owns the decision: whether a given search parameter identity and operator may use a single-column rewrite.
- The **SQL layer** owns translation: once policy approves the case, emit the corresponding one-column predicate.

This keeps parameter eligibility separate from SQL expression building and avoids encoding rollout rules directly inside SQL visitors.

### 2. Search parameter behavior registry

Add a code-owned registry keyed by search parameter identity.

For v1, entries are explicit and checked into the repo. The key should be based on the search parameter identity already used by the server to distinguish parameters, such as URL/code/base resource.

Each entry describes **behavior**, not raw SQL. Example shape:

- `Behavior = SinglePointDateTime`
- `SupportedOperators = eq, gt, ge, lt, le`

The registry is intentionally structured so the source of these entries can move later from code to metadata or config without changing the SQL contract.

### 3. Single-point rewrite policy

Introduce a small policy object or service that converts:

- search parameter identity
- operator
- relevant expression context

into a rewrite decision.

Representative decisions:

- `NoRewrite`
- `RewriteUsingEndDateTimeEquality`
- `RewriteUsingEndDateTimeLowerBound`
- `RewriteUsingStartDateTimeUpperBound`

This policy is the reusable seam for future growth. The allowlist feeds the policy in v1, but the SQL layer should not need to know whether the decision came from hardcoded entries, metadata, or configuration.

### 4. SQL consumption

The SQL generator continues to build the normal search expression, then consults the policy for the current search parameter and operator.

- If the policy returns `NoRewrite`, preserve today's generic two-column behavior.
- If the policy returns a supported rewrite decision, emit the corresponding one-column predicate.

The allowlist must not directly hardcode SQL text. It declares which behavior is allowed. The SQL layer translates that approved behavior into the actual SQL expression shape.

## Operator semantics

For `SinglePointDateTime`, v1 supports only a small explicit operator table:

- `eq` -> one-column equality
- `gt` / `ge` -> one-column lower-bound rewrite on the later/upper stored column for that behavior
- `lt` / `le` -> one-column upper-bound rewrite on the earlier/lower stored column for that behavior
- `ap`, `sa`, `eb`, composites, and any ambiguous case -> `NoRewrite`

The safety rule is strict:

> If policy cannot prove the rewrite is semantics-preserving for that parameter and operator, it must return `NoRewrite`.

Fallback is always the existing generic SQL path, never an error.

Operationally:

- allowlisted + supported operator -> one-column rewrite
- allowlisted + unsupported operator -> generic path
- not allowlisted -> generic path

This preserves correctness by making the worst case a missed optimization instead of a changed result set.

## Implementation shape

Build the next implementation from `main`.

Suggested order:

1. Add the behavior registry and policy layer.
2. Seed the registry with the initial allowlisted parameter identities.
3. Teach the SQL generator to ask policy whether to use:
   - the current generic two-column generation, or
   - the new one-column rewrite for supported operators.
4. Reuse pieces of the existing branch only if they are still directly useful; do not carry forward `IsDateOnly` or its resolver plumbing by default.

## Testing

### Unit tests

- Policy tests: search parameter identity + operator -> rewrite decision or `NoRewrite`.
- SQL rewrite tests: each supported operator emits the expected one-column predicate.
- Negative tests: non-allowlisted parameters, unsupported operators, approximate operators, and composite cases all stay on the generic path.

### End-to-end tests

- For each allowlisted parameter/operator, verify result-set equivalence between:
  - the new policy-driven rewrite path, and
  - the generic two-column path.

The correctness bar is that the optimized path returns the same rows as the generic path.

## Rollout

- Code-owned allowlist in v1.
- Small initial entry set.
- Generic fallback for everything not explicitly approved.
- Success is measured by improved plans and latency for allowlisted cases with no result changes.

## Rationale

This design is intentionally narrower than a full point-vs-range SQL redesign and intentionally broader than the current date-only branch.

It is narrower because rollout stays behind a code allowlist and avoids tricky operators.
It is broader because the abstraction is no longer tied to `birthdate` or to `date-only` as a type concept. The reusable concept is **single-point search behavior**.

That gives a simple v1 path while still creating the right architectural seam for future expansion.

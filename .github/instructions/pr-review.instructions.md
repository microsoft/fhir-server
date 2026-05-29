---
applyTo: "**"
---

# PR review instructions

When reviewing pull requests in this repository, use this focused review workflow in addition to the repository-wide Microsoft FHIR Server instructions.

## Review scope

- Review only the pull request diff or the explicitly requested file/range.
- Prioritize changed behavior over unchanged surrounding code.
- Do not request broad refactors unless they directly reduce review risk for the changed code.
- Include file and line references for actionable findings.
- Do not invent validation results. If a test, build, or command was not run, say so plainly.

## Review facets

### 1. General correctness

- Check for real bugs: incorrect logic, broken FHIR semantics, concurrency issues, null handling mistakes, security regressions, data-loss risks, and backwards-incompatible behavior.
- Verify changes follow existing architecture boundaries across API, Core, shared components, Cosmos DB, and SQL Server.
- Confirm new behavior aligns with FHIR standards and existing Microsoft FHIR Server conventions.

### 2. Tests

- Evaluate behavioral coverage, not line coverage.
- Look for missing tests around critical paths, edge cases, negative cases, async or concurrency behavior, persistence behavior, and FHIR-version-specific behavior.
- Prefer xUnit tests that use Arrange-Act-Assert and NSubstitute for external dependencies.
- Request E2E tests only when the change affects externally observable API behavior or integration flows.

### 3. Error handling and silent failures

- Flag catch blocks, fallbacks, default values, retries, or null-handling that could hide failures.
- Verify errors are surfaced, logged, or propagated according to existing repository patterns.
- Treat broad catches, empty catches, swallowed exceptions, and success-shaped fallbacks as high-risk unless clearly justified.

### 4. Comments and documentation

- Check that new or changed comments accurately describe the code.
- Flag comments that restate obvious code, contradict implementation, or are likely to rot.
- For public API or public members, verify XML documentation expectations from repository instructions.

### 5. Type design

- Review new or changed types for clear invariants, explicit state, and minimal invalid states.
- Prefer domain-specific types and existing helpers over loosely typed strings, dictionaries, or unnecessary casts.
- Check that public APIs are understandable without reading implementation internals.

### 6. Simplification

- After correctness, identify unnecessary complexity, duplication, clever control flow, or over-generalized abstractions.
- Suggest simplifications only when they preserve behavior and reduce maintenance risk.

## Output expectations

Report only findings that matter for merge readiness. Group findings by severity:

- Critical: bugs, security issues, data loss, broken FHIR behavior, or changes likely to fail production.
- Important: missing required tests, risky error handling, significant maintainability problems, or architecture violations.
- Minor: low-risk cleanup that would improve clarity.
- Positive observations: specific strengths in the changed code.
- Validation notes: commands reviewed or actually run; write "Not run" for anything not executed.

If no high-confidence findings exist, say that the review found no merge-blocking issues and mention the main areas checked.

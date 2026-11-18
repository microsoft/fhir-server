---
mode: 'agent'
description: 'Audit the current branch for authorization gaps of a named pattern, or for all patterns in the security review rules'

---

# Security Variant Sweep

Your goal is to find places in the existing code where a caller can read, change, or delete data beyond what their role, SMART scopes, and compartment allow. Pull request review only covers changed lines. This sweep covers code that no pull request is touching.

## Input

Either a pattern to hunt for, such as a recently fixed defect ("background job re-runs a search without the submitter's restrictions"), or nothing, in which case cover every numbered rule in the security instruction files.

## Rules to apply

Read these files first. They define the invariant and the patterns:

- `.github/instructions/fhir-security.instructions.md`
- `.github/instructions/security-search.instructions.md`
- `.github/instructions/security-background-operations.instructions.md`
- `.github/instructions/security-bundles-conditional.instructions.md`
- `.github/instructions/security-smart-scopes.instructions.md`

## Method

1. List the entry points for the pattern. Start from the sinks: every consumer of `ISearchService`, `IFhirDataStore`, `IDeletionService`, and `IBulkUpdateService`, every `IsBackgroundTask = true`, every `CheckAccess` call, and every branch on `KnownResourceTypes.All` or `ApplyFineGrainedAccessControl`.
2. For each one, trace backwards to the request that can reach it and forwards to the data it returns or changes. Write down the caller, the input they control, the check that runs, and the check that is missing.
3. Compare each path with the ordinary path for the same data, with the other storage provider, and with the other FHIR versions.
4. Try to disprove each candidate. Look for a check in a caller, a filter, middleware, or the data layer that already covers it. Drop candidates that are covered.
5. For each candidate that survives, write a unit or integration test with synthetic data: one caller that must succeed and one that must be refused. Run it. A candidate without a failing test is a lead, not a finding.

## Rules of engagement

- Use synthetic data and local test fixtures only. Do not probe a deployed service or use real patient data or credentials.
- Do not open a public issue, pull request, or comment that describes an unfixed flaw. Report findings to the maintainer who ran the sweep, for the private security advisory process.

## Output

For each finding: the file and line, the caller and the scope or role they need, the request, what they gain, the test that demonstrates it, and the rule it falls under. Then list the leads you could not confirm and what evidence is missing. State which providers, FHIR versions, and configurations you covered and which you did not.

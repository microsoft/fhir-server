---
applyTo: "**"
excludeAgent: "cloud-agent"
---

# FHIR Security Review: Core Rules

These rules apply to every pull request. The other `security-*.instructions.md` files in this directory add rules for the paths they match. `AGENTS.md` and `REVIEW.md` still apply.

## The invariant

Every code path that reads, returns, counts, changes, or deletes a resource must enforce the current caller's `DataActions`, SMART scope restrictions, and compartment (`AccessControlContext`). A check at the controller or request handler does not cover data that the code reaches later by another route.

## Flag these changes

For each one, name the caller, the input, the missing check, and what the caller gains.

1. **Data access outside the authorizing handler.** A new call to `ISearchService`, `IFhirDataStore`, `IDeletionService`, or `IBulkUpdateService` from a resolver, service, behavior, or job. Ask where the caller's authority for that lookup or write is checked.
2. **Admission checks used as authorization.** `CheckAccess(DataActions.All)` compared with `DataActions.None` only proves the caller has some permission. Each operation behind it needs its own action check.
3. **Checks on the route type only.** The code authorizes the resource type in the route and then touches other types through `_include`, `_revinclude`, references, bundle entries, or cascades. Each touched type needs the action being performed on it.
4. **Wildcards that skip a filter.** A branch that skips work when the scope is `KnownResourceTypes.All` or `*`. A wildcard answers which types are allowed. It does not answer which patient's data is allowed, so the compartment filter must still apply.
5. **Absent context read as unrestricted.** `AccessControlContext?.ApplyFineGrainedAccessControl == true` is false when the context is missing. Confirm a caller-originated request cannot arrive without it. A missing patient, `fhirUser`, or scope must refuse the request.
6. **Trust taken from the request.** Authority must come from the validated token and server state. A query parameter, header, route value, body field, or continuation token must not select internal behavior or claim to be trusted. `IsBackgroundTask` must only be set by server code.
7. **Enforcement behind a switch.** A new or changed flag that relaxes enforcement, such as `EnableSmart*Restriction`, `ErrorOnMissingFhirUserClaim`, or `EnableSmartWithoutAuth`. The default must enforce, and the disabled path must be deliberate.
8. **A feature that cannot enforce SMART restrictions.** A new operation or search feature that returns or changes data must apply `AccessControlContext` or refuse SMART-restricted callers. It must not run unrestricted.
9. **One provider or version only.** A check added to SQL Server and not to Cosmos DB, or to one of STU3, R4, and R5.
10. **Failures turned into success.** A `catch` or fallback that turns an authorization failure into an empty result, a default value, or a broader query.

## Inputs, outputs, and limits

- Keep untrusted values out of SQL, shell, and other executable syntax. Validate dynamic identifiers and operators. Parameterized values do not make a caller-chosen query structure safe.
- For outbound calls (import sources, references, callbacks, OAuth redirects), check the final decoded destination at connection time, including redirects. Check the recipient before the server sends its own credentials.
- Bound the size, depth, time, and total work of parsers, regular expressions, FHIRPath, nested searches, bundles, and jobs.
- Keep patient data, tokens, connection strings, and signed URLs out of logs, errors, traces, and test artifacts.
- For build, dependency, and workflow changes, check provenance, permissions, secrets, and any weakened scan or test step.

## Evidence

- A security finding needs a concrete boundary and an unauthorized outcome. If you cannot trace one, ask a question instead of asserting a vulnerability.
- Do not report a missing best practice, a guessed deployment, or a past advisory as a defect in this revision.
- A new check needs a test that fails without it. Use two principals or two patients: one request that must succeed and one that must be refused. Denying everyone does not pass.
- Tests with mocked authorization or security disabled do not prove enforcement. Say which provider, FHIR version, and configuration the tests exercise.

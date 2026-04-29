# Import URL Validation Review Design

## Problem

This branch is intended to fix a security bug in `$import` request validation. The bug is that `ImportController.ValidateImportRequestConfiguration()` accepted any absolute `input[n].url` without a host allowlist, which let a caller with import permissions drive the server's managed identity through the Azure Storage SDK to arbitrary hosts. The review must confirm the PR restores the intended invariant and fix any gaps found on this branch.

## Scope

### In scope

- Review the branch against the intended security invariant for import input URLs.
- Verify the invariant holds when integration storage is configured by either `StorageAccountUri` or `StorageAccountConnection`.
- Verify unit-test and documentation coverage for the accepted and rejected URL shapes.
- Review adjacent issues directly mentioned in the report when they are in the same import path, including reflected import diagnostics.
- Fix gaps on this branch when they are part of the same security boundary or a tightly coupled follow-up.

### Out of scope

- Broad refactoring outside the import URL validation path.
- Unrelated security cleanup elsewhere in the repository.

## Approved approach

Use an invariant-first security review.

1. Compare the branch against the exact reported invariant.
2. Check whether the implementation actually blocks the token-forwarding path across supported configuration styles.
3. Confirm tests and docs describe the real security contract.
4. Assess adjacent issues from the report and either fix them if tightly coupled or record them as follow-up work.

## Security invariant

Every accepted `input[n].url` must be constrained to the configured integration storage account so the server never builds an authorized `BlobClient` for an attacker-controlled host. At minimum, rejected inputs include:

- null URLs
- relative URLs
- URLs with query strings or SAS tokens
- URLs targeting an unconfigured host

Accepted inputs must remain valid for the configured integration storage endpoint under both direct-URI and connection-string configuration.

## Review method

The review will judge the branch on four axes:

1. **Validation boundary**: what URL forms are accepted and rejected in `ImportController.ValidateImportRequestConfiguration()`.
2. **Configuration derivation**: how the allowlisted storage endpoint is derived from `IntegrationDataStoreConfiguration`.
3. **Coverage**: whether unit tests prove both allowed and rejected cases.
4. **Adjacent fallout**: whether directly related paths, such as reflected import diagnostics, still expose sensitive backend detail.

## Components under review

### Primary component

- `src/Microsoft.Health.Fhir.Shared.Api/Controllers/ImportController.cs`

This is the trust boundary where import input must be rejected before any background work or outbound storage access occurs.

### Secondary components

- Import controller unit tests covering request validation behavior.
- Any helper logic that derives the configured storage endpoint from `StorageAccountUri` or `StorageAccountConnection`.
- `docs/BulkImport.md` to ensure the user-facing contract matches the enforcement.
- The import status / diagnostics path cited in the report when evaluating the adjacent issue.

## Expected change surface

The preferred fix remains small and explicit:

- controller validation logic
- helper logic needed to compute the canonical configured endpoint
- targeted unit tests
- targeted documentation updates

If the diagnostics reflection issue is fixed here, it should stay similarly narrow and remain confined to the import-status/error-reporting path.

## Data flow and failure behavior

The review follows the real exploit path:

`$import` request body -> `ValidateImportRequestConfiguration()` -> import request creation/queueing -> orchestration/data-store access -> `BlobClient` construction

Success means the path is cut off at controller validation with a deterministic client validation failure before any outbound access can be attempted to an untrusted host.

## Test expectations

The branch should demonstrate:

- valid requests succeed for configured storage endpoints
- invalid requests fail for host mismatch, relative URLs, and query-bearing URLs
- connection-string parsing does not allow arbitrary endpoint overrides to widen trust
- documentation states that import URLs must target the configured integration storage account

If the diagnostics issue is fixed here, tests should also prove that import status responses no longer reflect sensitive backend/header details while preserving actionable error reporting.

## Success criteria

The branch is correct when all of the following are true:

1. The reported token-forwarding path is blocked at input validation.
2. The allowlist source is derived from trusted configuration only.
3. Test coverage proves the accepted and rejected cases above.
4. Documentation matches the enforced behavior.
5. Any tightly coupled adjacent issue found during review is either fixed here or explicitly called out as separate follow-up work.

## Deliverable

Produce an assessment of whether the PR matches the original prompt and, if not, update this branch to close the gaps.

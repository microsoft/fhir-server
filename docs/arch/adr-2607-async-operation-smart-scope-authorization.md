# ADR-2607: SMART System Export Authorization

**Status**: Accepted
**Date**: 2026-07-14
**Feature**: SMART export authorization

## Context

FHIR Bulk Data export is an asynchronous operation. The initiating request creates the user-facing export job while other requests are used to fetch status/results and to cancel the operation. Existing controller authorization checks the export action but does not authorize those operation URLs against the FHIR Resource types set represented by the persisted job. SMART on FHIR uses resources types for authorization heavily. This results in a potential security issue.

The [FHIR Bulk Data Access IG](https://hl7.org/fhir/uv/bulkdata/export.html) defines export as a backend-services operation using SMART system scopes. Patient and user SMART scopes describe interactive clinical access and cannot safely authorize an asynchronous bulk extraction. Existing RBAC export permission and non-SMART behavior must remain unchanged.

## Options Considered

1. **Allow export from all SMART users. Persist patient or user creator context** - Bind jobs to a SMART compartment and revalidate that identity on later requests. *(rejected: adds authorization identity to job metadata and treats Bulk Data export as interactive clinical access)*
1. **Require SMART system scopes and authorize persisted resource types** - Limit SMART export to backend system scopes and revalidate each job's resource set. *(chosen)*

## Decision

Requests subject to SMART fine-grained access control may create, read, or cancel export jobs only through system scopes. Patient and user contexts are rejected. RBAC `Export` permission remains an independent prerequisite.

The behavior is enabled by default with `FhirServer:CoreFeatures:EnableSmartExportScopeAuthorization`. Setting it to `false` is a temporary compatibility override: create requests preserve the requested `_type` unchanged, and status or cancellation skips SMART scope validation. RBAC `Export` authorization continues to be required before either path.

Authorization distinguishes two independent concerns, both of which must be satisfied:

- **Population-selection authorization** — the resource type(s) implied by the export *route* (e.g. `Group` requires access to `Group` and `Patient`, regardless of the resources actually output).
- **Output-type authorization** — the resource type(s) that will appear in the export output, as an explicit `_type` or, when omitted, an effective `_type` inferred and persisted from the caller's scopes.

A system wildcard export-read scope satisfies both concerns unconditionally and leaves the export unconstrained (no `_type` is persisted). Otherwise:

- **Explicit `_type`** must be a fully authorized subset: every requested type must be covered by an unconstrained system scope, in addition to the route's selection prerequisites. Overreach — any requested type not covered — is Forbidden.
- **Omitted `_type`** is Allowed for a partial system scope, provided the route's selection prerequisites are met. The effective `_type` is not left wildcard; it is deterministically inferred to include every resource-specific type whose matching unconstrained system scopes together provide complete export-read access, and that comma-separated list is persisted on the job. If no resource type is eligible for inference, the request is Forbidden.

SMART v1 coverage requires read plus export. SMART v2 coverage requires read-by-id, search, and export, equivalent to `rs` plus export. Search-parameter-constrained scopes do not authorize export (they are never eligible for inference either).

### Route selection prerequisites

| Route | Population-selection prerequisite (independent of output `_type`) |
| --- | --- |
| `$export` (system-level) | None |
| `Patient/$export` | `Patient` |
| `Group/{id}/$export` | `Group` and `Patient` |

An unconstrained `system/*` scope satisfies every route's prerequisite. These prerequisites apply in addition to, and independently of, every explicit or inferred output `_type` — a caller must satisfy both to create the job.

Status and cancellation re-derive both concerns from the persisted job: the route prerequisite is re-derived from the persisted `ExportType`, and the output-type requirement is re-derived from the persisted effective `_type` (explicit or inferred) plus any completed output types. A legacy or otherwise unconstrained job with no persisted `_type` still requires wildcard access. Unauthorized job access is returned as not found to avoid an existence oracle.

### Planned request behavior

| SMART context | Scope resource coverage | Create without `_type` | Create with explicit `_type` | Status or cancel |
| --- | --- | --- | --- | --- |
| Patient | All resources | Forbidden | Forbidden | Not found |
| Patient | Selected resources | Forbidden | Forbidden | Not found |
| User | All resources | Forbidden | Forbidden | Not found |
| User | Selected resources | Forbidden | Forbidden | Not found |
| System | All resources | Allowed (unconstrained; no `_type` persisted) | Allowed | Allowed for any export job |
| System | Selected resources, route prerequisite met | Allowed; effective `_type` inferred and persisted from eligible scopes | Allowed only when every requested type is covered | Allowed only when every persisted requested or output type, and the route prerequisite for the persisted `ExportType`, are covered; otherwise not found |
| System | Selected resources, route prerequisite not met | Forbidden | Forbidden | Not found |

"Allowed" assumes the existing RBAC `Export` check succeeds and the SMART scope grants the required export-read actions. All-resource coverage means an unconstrained `system/*` scope. Selected-resource coverage means one or more unconstrained `system/{resourceType}` scopes. SMART v1 requires read plus export; SMART v2 requires read-by-id, search, and export. Search-parameter-constrained scopes do not provide export coverage. Non-SMART requests retain their existing behavior.

## Consequences

- SMART patient and user applications cannot use Bulk Data export.
- A partial system caller that omits `_type` is no longer rejected outright: the export proceeds against a deterministic, narrowed, persisted effective `_type` inferred from its scopes.
- Partial system access can export an explicit authorized type set without receiving wildcard access; overreach beyond the authorized subset remains Forbidden.
- Route selection prerequisites (`Patient` for `Patient/$export`, `Group`+`Patient` for `Group/{id}/$export`) are enforced in addition to output-type authorization, both at creation and at status/cancel, independently reducing accidental over-broad access via a narrowly scoped route.
- Legacy and other jobs without a persisted `_type` remain visible only to wildcard system callers.
- Export job records do not persist SMART authorization identity or compartment metadata; the effective `_type` is persisted as ordinary job authorization state.
- Unauthorized job access is indistinguishable from an unknown job identifier.
- Completed output types can tighten later authorization but cannot weaken the no-`_type` wildcard rule.
- RBAC and non-SMART export behavior are unchanged.
- A default-on compatibility switch can temporarily disable SMART export scope authorization while preserving RBAC export permission checks.
- Other asynchronous operation types are outside this decision and retain their existing behavior.

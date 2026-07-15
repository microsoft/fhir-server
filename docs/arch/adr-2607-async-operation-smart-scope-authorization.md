# ADR-2607: SMART System Export Authorization

**Status**: Proposed
**Date**: 2026-07-14
**Feature**: SMART export authorization

## Context

FHIR Bulk Data export is asynchronous: the initiating request creates a job, while separate operation URLs expose status, result metadata, and cancellation. Existing route-level authorization checks the export action but does not authorize those operation URLs against the resource set represented by the persisted job. Predictable job identifiers can therefore expose or mutate an export job unless authorization is repeated after loading its metadata.

The [FHIR Bulk Data Access IG](https://hl7.org/fhir/uv/bulkdata/export.html) defines export as a backend-services operation using SMART system scopes. Patient and user SMART scopes describe interactive clinical access and cannot safely authorize an asynchronous bulk extraction. Existing RBAC export permission and non-SMART behavior must remain unchanged.

## Options Considered

1. **Rely on route-level export authorization** - Authorize only the export action on each route. *(rejected: status and cancellation would not be checked against persisted job resource types)*
2. **Persist patient or user creator context** - Bind jobs to a SMART compartment and revalidate that identity on later requests. *(rejected: adds authorization identity to job metadata and treats Bulk Data export as interactive clinical access)*
3. **Require SMART system scopes and authorize persisted resource types** - Limit SMART export to backend system scopes and revalidate each job's resource set. *(chosen)*

## Decision

Requests subject to SMART fine-grained access control may create, read, or cancel export jobs only through system scopes. Patient and user contexts are rejected. RBAC `Export` permission remains an independent prerequisite. A system wildcard export-read scope may create an export without `_type`; otherwise creation requires a nonempty explicit `_type`, and every requested type must be covered by an unconstrained system scope.

SMART v1 coverage requires read plus export. SMART v2 coverage requires read-by-id, search, and export, equivalent to `rs` plus export. Search-parameter-constrained scopes do not authorize export. Status and cancellation derive requirements from persisted `_type` and defensively include completed output types. A job without explicit `_type` always requires wildcard access, even when current output contains only a subset. Unauthorized job access is returned as not found to avoid an existence oracle.

### Planned request behavior

| SMART context | Scope resource coverage | Create without `_type` | Create with explicit `_type` | Status or cancel |
| --- | --- | --- | --- | --- |
| Patient | All resources | Forbidden | Forbidden | Not found |
| Patient | Selected resources | Forbidden | Forbidden | Not found |
| User | All resources | Forbidden | Forbidden | Not found |
| User | Selected resources | Forbidden | Forbidden | Not found |
| System | All resources | Allowed | Allowed | Allowed for any export job |
| System | Selected resources | Forbidden | Allowed only when every requested type is covered | Allowed only when the job has explicit `_type` and every persisted requested or output type is covered; otherwise not found |

"Allowed" assumes the existing RBAC `Export` check succeeds and the SMART scope grants the required export-read actions. All-resource coverage means an unconstrained `system/*` scope. Selected-resource coverage means one or more unconstrained `system/{resourceType}` scopes. SMART v1 requires read plus export; SMART v2 requires read-by-id, search, and export. Search-parameter-constrained scopes do not provide export coverage. Non-SMART requests retain their existing behavior.

## Consequences

- SMART patient and user applications cannot use Bulk Data export.
- Partial system access can export an explicit authorized type set without receiving wildcard access.
- Legacy and new jobs without explicit `_type` remain visible only to wildcard system callers.
- Export job records do not persist SMART authorization identity or compartment metadata.
- Unauthorized job access is indistinguishable from an unknown job identifier.
- Completed output types can tighten later authorization but cannot weaken the no-`_type` wildcard rule.
- RBAC and non-SMART export behavior are unchanged.
- Other asynchronous operation types are outside this decision and retain their existing behavior.

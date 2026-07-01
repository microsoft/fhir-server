# ADR-2607: Async Operation SMART Scope Authorization

**Status**: Proposed
**Date**: 2026-07-01
**Feature**: Async operation SMART scope authorization

## Context

FHIR asynchronous operations expose status, result metadata, and cancellation routes for jobs such as export, import, reindex, bulk delete, and bulk update. These routes are not always tied to a specific request resource type, so the existing route-level authorization can allow narrow SMART clinical scopes to access system-level job state or initiate job cancellation.

The security requirement is to make async job access depend on SMART system scopes, not only on the route action. Export status has a narrower safe model because export metadata is resource typed; other async job types and all cancellation operations require broad system privileges because they can expose or affect operational state across the service.

## Options Considered

1. **Rely on existing route-level authorization** - Keep current `DataActions` checks only. *(rejected: resource-type-less async routes can collapse SMART restrictions too broadly)*
2. **Bind every async job to its creator and require owner-only access** - Store and compare the creating principal for all job status and cancel operations. *(rejected: useful long term, but it does not directly express the SMART system-scope requirement and requires job schema/compatibility work)*
3. **Add async-operation SMART scope validation at handler boundaries** - Keep coarse route checks, then validate SMART system scopes using the loaded job metadata. *(viable)*

## Decision

We will add a focused async-operation SMART scope validator at the handler layer. Existing route-level authorization remains as the coarse gate, and each async status or cancellation handler applies an additional SMART-aware check before returning job data or mutating job state.

Export status may be accessed with SMART system scopes restricted to the resource types represented by the export job metadata or results. SMART v1 requires read access, and SMART v2 requires both read-by-id and search (`rs`). If the export metadata includes a resource type outside the caller's SMART system scope, the request is forbidden. All non-export job status/result fetches, and every cancellation endpoint including export cancellation, require all-resource SMART system read and write access.

## Consequences

- Async operation authorization becomes explicit and resource-aware instead of relying on base operation routes with no resource type.
- Export status remains usable for appropriately scoped system clients without requiring all-resource access when the export is resource constrained.
- Import, reindex, bulk delete, bulk update, and all cancellation paths require stronger all-resource system scopes, reducing job-state disclosure and unauthorized operational changes.
- The implementation must derive export resource requirements from job metadata and outputs, including empty Patient and Group exports.
- Future async job types must choose whether they can safely support resource-aware status access or should use the all-resource read/write rule.

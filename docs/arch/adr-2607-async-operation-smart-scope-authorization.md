# ADR-2607: SMART Patient and User Export Authorization

**Status**: Proposed
**Date**: 2026-07-14
**Feature**: SMART export compartment authorization

## Context

FHIR Bulk Data export is asynchronous: the initiating request creates a job, while separate operation URLs expose status, result metadata, and cancellation. Existing route-level authorization checks the export action but does not bind those operation URLs to the SMART compartment that initiated the job. Predictable job identifiers can therefore expose or mutate another caller's export job unless authorization is repeated against persisted job context.

The [FHIR Bulk Data Access IG](https://hl7.org/fhir/uv/bulkdata/export.html) defines Patient-level export, while [SMART App Launch scopes](https://hl7.org/fhir/smart-app-launch/scopes-and-launch-context.html) and FHIR compartment membership constrain the resources visible to patient and user applications. Non-SMART and SMART system export behavior must remain compatible.

## Options Considered

1. **Rely on route-level export authorization** - Authorize only the export action on each route. *(rejected: status and cancellation are not bound to the initiating SMART compartment)*
2. **Require SMART system scope for every export** - Disallow patient and user export requests. *(rejected: prevents standards-aligned compartment export scenarios)*
3. **Bind patient and user jobs to target and creator context** - Validate the Patient target and resource scopes at creation, persist normalized SMART context, and revalidate loaded jobs. *(chosen)*

## Decision

SMART patient and user scopes may create only `Patient/{id}/$export` jobs with an explicit `_type`. Patient scope requires the target ID to equal the Patient `fhirUser`; user scope requires the target Patient to belong to the user's SMART compartment. Every selected resource type must have export-read access: SMART v1 read or SMART v2 read-by-id plus search, together with export permission. Execution restricts the top-level Patient search to the requested Patient ID.

Patient- and user-created jobs persist the normalized `fhirUser` resource type and ID. Status and cancellation validate that persisted context and resource coverage after loading the job; a context mismatch is returned as not found to avoid disclosing job existence. SMART system jobs remain resource-scope based and are not creator-bound. Non-SMART export behavior is unchanged.

## Consequences

- Patient and user exports are constrained to an explicit Patient target and explicitly selected resource types.
- Status and cancellation cannot cross normalized patient or user contexts.
- Export job records and processing children carry target and creator-context metadata.
- Unauthorized job access is indistinguishable from an unknown job identifier.
- User-scope creation requires a SMART compartment membership search.
- Other asynchronous operation types are outside this decision and retain their existing behavior.

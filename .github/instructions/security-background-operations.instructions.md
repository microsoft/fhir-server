---
applyTo: "src/**/Features/Operations/**,src/Microsoft.Health.TaskManagement/**,src/Microsoft.Health.Fhir.Azure/**,src/**/Features/Resources/Delete/**,src/**/Features/Resources/Upsert/**,src/**/Features/Persistence/**"
excludeAgent: "cloud-agent"
---

# FHIR Security Review: Bulk and Background Operations

Export, import, reindex, bulk delete, and bulk update authorize the caller in a request handler and do the work later in a job. The job runs with `new FhirRequestContext { IsBackgroundTask = true }`, which has no principal and an empty `AccessControlContext`. Any search, delete, or update the job performs is unrestricted unless the code restores the restrictions. That is correct for system work such as reindex. It is a defect when the job acts on criteria a restricted caller supplied.

## Creating the job

1. **Restrictions must survive the queue.** If the job acts on criteria the caller supplied, one of these must be true. The job definition (`BulkDeleteDefinition`, `BulkUpdateDefinition`, `ExportJobRecord`, and similar) stores the submitter's scope and compartment restrictions and the worker applies them. Or the create handler refuses callers it cannot restrict, as `EnableSmartBulkDeleteRestriction` does. Flag a job that does neither.
2. **Authorize everything the job will touch.** The create handler must check the action being performed (`Delete`, `HardDelete`, `Update`, `Export`, and so on) for every resource type the job can reach. That includes types selected by `_include` and `_revinclude`, and it applies after `excludedResourceTypes` and similar options are taken into account.
3. **Finding resources needs search authority.** A job that selects its targets by search criteria needs the caller's read or search permission for those types in addition to the write action.
4. **Protected resources.** Profile-defining resources need `DataActions.EditProfileDefinitions`, whether the job targets them directly or through an include.
5. **Operation permissions are narrow.** `DataActions.Reindex`, `Import`, or `Export` does not grant read access to every resource or representation the operation can produce.
6. **The definition is trusted state.** Fields that carry authorization into the job must be set by server code from the validated request. Request input must not be able to set them.

## Running the job

7. **The worker must act on what was authorized.** If the worker runs the search again, it must produce the set the handler authorized. Flag a worker that re-parses raw parameters in the background context and gets a broader result.
8. **Child jobs, retries, and resumes keep the restrictions.** Check that orchestrator-to-worker hand-offs and continuation state do not drop them.
9. **Report honestly.** A resource that was skipped for authorization must be counted as skipped or cause a failure, as the operation's contract says. It must not be reported as processed.

## Status, cancel, and results

10. **A job ID is not a credential.** Status, cancel, and result endpoints must check the caller's permission for that operation and confirm the ID belongs to the expected job type. Do not require that only the creator can see a job unless the operation's contract says so.
11. **Result links are credentials.** Issue signed URLs only after authorization, and keep them out of logs.
12. **Outbound storage.** Import sources and export destinations must be limited to the configured storage account, and checked at connection time.

## Tests to ask for

- A SMART-restricted caller and an unrestricted caller submit the same job. Assert what was changed or exported in storage, not only the job status.
- A request that reaches a second resource type through `_include` or `_revinclude` without the action on that type.
- Status and cancel calls from a caller who lacks the operation's permission.

# Verify resource ID boundaries in PR and CI

Use the SQL jobs for Stu3, R4, and R5. Each SQL container app serves the
`Standard64` baseline on its default route and every other revision profile on a
zero-weight labeled revision of the same app. Do not run `ci-deploy.yml` or
definition 29 to create these endpoints.

## Check a pipeline run

1. Open the ordinary `test{Version}Sql` stage. Its main E2E process runs the
   `Standard64` boundary cases against the default route alongside the existing
   suite.
2. Open `test{Version}SqlProfiles`. It starts after every ordinary SQL job for
   that version, including reindex and bulk update. Its `prepare` job converges
   one labeled revision per profile in `build/jobs/scripts/RevisionProfiles.psd1`
   and captures each binding. The `Profile` matrix then runs one job per profile,
   one at a time, against that profile's label URL.
3. Inspect `ResourceIdEndpoint_{Version}_{Profile}_{Job}_attempt{N}`. Check the
   revision, label, URL, image, readiness, template fingerprint, and baseline
   revision. Every profile must run the same SHA-256 image digest as the baseline,
   not merely a matching build tag. These artifacts contain no credentials. After
   the tests, the job resolves the selected binding again with `-Expect`, so a
   revision, URL, image, template, or baseline route that changed during the run
   fails the job.
4. Inspect `ResourceIdResults_{Version}_{Profile}_{Job}_attempt{N}`. Require
   `Standard64_64_Accept`, `Standard64_65_Reject`, and `Standard64_128_Reject`
   from the ordinary run. Require `Long128_64_Accept`, `Long128_65_Accept`,
   `Long128_128_Accept`, and `Long128_129_Reject` from the `Long128` run.
5. To retry a failed profile stage, use **Rerun failed stages**. A new stage
   attempt creates new labeled revisions, so a failed immutable revision never
   blocks the retry. Consumers use the bindings captured by their own `prepare`
   job and never recompute them.

The checker requires each exact case's final outcome to be `Passed`. Missing,
skipped, failed, stale, wrong-profile, and unexpected boundary results fail the
job. A passing retry can replace an earlier failure, but duplicate cases cannot
replace a missing case.

## How a profile revision is built

`build/jobs/scripts/Sync-SqlRevisions.ps1` is the only entry point. It has three
goals:

- `Converge` with `-Image` deploys a new baseline revision from the routed
  baseline's template and moves the default route to it. Without `-Image` it
  creates the catalog's profile revisions at zero weight and returns their
  bindings and the job matrix.
- `Resolve` returns the selected revision for one profile with its readiness,
  image, template fingerprint, and endpoint settings. A labeled profile requires
  the producer's captured `-Binding` and checks the label, URL, declared
  settings, and baseline route against it. `-Expect` also requires the captured
  image and template fingerprint.
- `Settle` copies this build's routed baseline to a `minReplicas=0` successor
  and retires this build's profile revisions.

Each new revision copies the named source revision's complete template and
changes only the declared image, environment, and scale values. The script does
not use `az containerapp revision copy`, because that command can inherit
containers from the latest revision instead of the named source. Default traffic
always routes to a named baseline revision, never to `latest`. Labels that
belong to other builds keep their bindings. Single-mode adoption changes the
mode and pins its healthy baseline in one configuration request. Existing
Multiple-mode apps with latest-based default routing or floating labels are
refused without writes. Every baseline source must be active, provisioned, and
healthy, including a settled baseline at minimum zero.

For an accepted asynchronous ARM write, the script waits for the operation to
complete before proceeding. It then verifies the resulting revision or traffic
state. Failed, canceled, or timed-out operations stop the job without repeating
the accepted write.

During each controller attempt, an unexpected change to the default revision,
its source template, or the revision mode fails before the next planned step.
The controller accepts its own verified mode change and baseline promotion.
This check detects observed changes. It does not provide a global lock or an
atomic compare-and-swap between the final read and a traffic write.

`Long128` enables long IDs, disables task hosting, disables database creation,
and disables automatic schema updates. It keeps normal SQL initialization
enabled. Use this endpoint only for synchronous HTTP boundary tests, not to
verify import, export, reindex, or bulk-update processing. The setup step rejects
a Key Vault secret that would override any declared revision setting.

## Add a profile

Add an entry to `Profiles` in `RevisionProfiles.psd1` with `Name`, `Slug`,
`CategoryFilter`, `ResourceIdProfile`, and `Environment`. The catalog rejects
other fields and any override of the SQL connection, security, Key Vault, Azure
identity, storage, data store, URL, or telemetry settings. The slug becomes part
of the revision name and label, so it must be one to eight lowercase letters or
digits, starting with a letter. The matrix grows with the catalog. No pipeline
edit is needed.

Use an alphanumeric `Name` of at most 100 characters, starting with a letter.
`None`, `Runtime`, and `Standard64` are reserved names. `ResourceIdProfile=None`
remains valid for a differently named profile with its own category. Ordinary SQL
jobs always select and recheck the named baseline, even when they do not run the
resource-ID contract.

## Run the checks locally

Run the self-contained PowerShell checks without Azure credentials. The revision
check fakes the ARM boundary and covers a second synthetic profile. The ARM
transport check also uses a local HTTP server for response and timeout tests.

```powershell
	& build/jobs/scripts/Test-RevisionProfiles.ps1
	& build/jobs/scripts/Test-RevisionArmTransport.ps1
	& build/jobs/scripts/Test-ResourceIdResults.ps1
	& build/jobs/scripts/Test-ResourceIdCleanup.ps1
```

To recheck downloaded TRX files, supply the UTC start time of their invocation.

```powershell
	& build/jobs/scripts/Assert-ResourceIdResults.ps1 `
		-ResultsDirectory ./downloaded-results `
		-Profile Long128 `
		-StartedUtc '2026-11-05T23:00:00Z'
```

To run the local seven-case configuration-binding matrix, build the selected E2E
project and use the MTP runner with `--filter-class '*Rest.ResourceIdLengthTests*'`.
Leave the remote SQL URL unset. This matrix requires local SQL and isolated
startup databases.

For an authorized remote run, use the pipeline setup and its selected revision.
Do not change URLs within a test process. The remote driver requires
`ResourceIdProfile`, `ResourceIdExpectedUrl`, the version-specific
`TestEnvironmentUrl_{Version}_Sql`, and the existing authentication variables.
An explicit profile without its HTTPS SQL URL fails before server creation. The
driver checks healthy storage, an authenticated HTTP 200 response, and the FHIR
version before the boundary requests.

## Inspect retained resources

Revisions are named `{app}--base-b{BuildId}a{Attempt}` for a deployed baseline,
`{app}--base-b{BuildId}a{Attempt}-m0` for a settled baseline, and
`{app}--{slug}-b{BuildId}a{Attempt}` for a profile. Profile labels are
`{slug}-b{BuildId}a{Attempt}`. A new profile copies its selected baseline's exact
image. Retained generations may use older images and retain live replicas.
Revisions share the app's identities, secrets, registry, ingress, storage, and
the `FHIR{Version}` database. A profile
revision creates no app, role assignment, storage account, registry, vault,
database, or network rule.

On successful PR runs, cleanup waits for the ordinary and profile stages. It
deletes the SQL app, then the shared version database. Failed and canceled runs
retain their resources.

CI redeploys each SQL app as a new baseline revision with the build's
digest-pinned image and the test minimum replica count. The generic scale-up and
scale-down steps skip the SQL apps. After every test stage succeeds, scale-down
settles each SQL app to a `minReplicas=0` baseline and retires this build's
profile revisions. Image promotion also waits for the profile stages. Settle
refuses an app whose routed baseline belongs to another build. A
`minReplicas=0` setting does not prove that replicas have stopped.
Settle requires full template equality before retiring a current-build profile
or baseline predecessor. A conflicting template is preserved and fails the stage.
Inspect retained revisions with `az containerapp revision list` and check the
[Container Apps limits](https://learn.microsoft.com/azure/container-apps/quotas)
before adding profiles. No cross-build cleanup is performed.

# Verify resource ID boundaries in PR and CI

Use the SQL jobs for Stu3, R4, and R5. Do not run `ci-deploy.yml` or definition 29
to create these endpoints.

## Check a pipeline run

1. Open the ordinary `test{Version}Sql` stage. Its main E2E process runs the
   `Standard64` boundary cases alongside the existing suite.
2. Open `test{Version}SqlLong128`. This stage starts after every ordinary SQL job
   for that version, including reindex and bulk update. It creates or updates
   only the variant app and its Key Vault Secrets User role.
3. Inspect `ResourceIdEndpoint_{Version}_{Profile}` and
   `Long128Deployment_{Version}`. Check the image, version, database, app name,
   ready revision, and URL. Both profiles must use the same SHA-256 image digest,
   not merely matching build tags. These artifacts contain no credentials.
4. Inspect the `ResourceIdResults_{Version}_{Profile}` TRX artifact. Require
   `Standard64_64_Accept`, `Standard64_65_Reject`, and `Standard64_128_Reject`.
   Require `Long128_64_Accept`, `Long128_65_Accept`, `Long128_128_Accept`, and
   `Long128_129_Reject` in the separate `Long128` run.
5. To retry a failed focused stage, rerun that stage against the retained baseline.
   The app name and vault role are deterministic. Each test invocation creates a
   fresh result directory and each HTTP case generates a new resource ID.

The checker requires each exact case's final outcome to be `Passed`. Missing,
skipped, failed, stale, wrong-profile, and unexpected boundary results fail the
job. A passing retry can replace an earlier failure, but duplicate cases cannot
replace a missing case.

## Run the checks locally

Run the self-contained PowerShell checks without Azure credentials.

```powershell
	& build/jobs/scripts/Test-ResourceIdResults.ps1
	& build/jobs/scripts/Test-ResourceIdProfile.ps1
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

For an authorized remote run, use the pipeline setup and its selected
`containerAppName`. Do not change URLs within a test process. The remote driver
requires `ResourceIdProfile`, `ResourceIdExpectedUrl`, the version-specific
`TestEnvironmentUrl_{Version}_Sql`, and the existing authentication variables.
An explicit profile without its HTTPS SQL URL fails before server creation.
The driver checks healthy storage, an authenticated HTTP 200 response, and the
FHIR version before the boundary requests.

## Inspect retained resources

PR variants use `f{BuildId}-{version}-l128`. CI variants use
`msh-fhir-ci-{version}-long128`. Do not append a suffix to the baseline app name.

The baseline owns `FHIR{Version}`, migrations, pool placement, the shared SQL
connection secret, storage, monitoring, and network settings. The variant reuses
the baseline image, environment, audience, UAMIs, storage URIs, and monitoring.
It has its own system identity and one vault-reader role because the application
loads Key Vault configuration with its system identity. It creates no storage
account, registry, vault, database, or network rule.

`Long128` enables long IDs, disables task hosting, disables database creation,
and disables automatic schema updates. It keeps normal SQL initialization
enabled. Use this endpoint only for synchronous HTTP boundary tests, not to
verify import, export, reindex, or bulk-update processing.

On successful PR runs, cleanup waits for both test stages. It removes the
variant-owned vault role and app, then the baseline app, then the shared database
once. Failed and canceled runs retain their resources. CI retains the variant,
includes its tests in image-promotion dependencies, and scales it with the other
apps after successful tests.

# NuGet Binary Closure Validation Design

## Summary

Add a blocking binary-closure validation gate to the NuGet packaging path used by both pull-request and continuous-integration builds. Each supported FHIR Web package will be restored into a temporary consumer project, published for each supported target framework, and checked with a pinned `checkbinarycompat` tool against a checked-in baseline.

This producer gate detects dependency closure defects introduced by OSS dependency or packaging changes. A second, authoritative gate in fhir-paas validates the final published application after all downstream package pins and overrides. Together they detect defects that otherwise appear only at runtime, including missing assemblies, version mismatches, missing types, and missing members. They do not replace semantic API compatibility analysis between released package versions.

## Goals

- Validate the supported FHIR server deployment roots from the OSS packaging job for every supported target framework.
- Validate the dependency closure that a package consumer receives after restore rather than only the assemblies embedded in the package archive.
- Run the same blocking check in PR and CI builds.
- Require fhir-paas to validate each final published application closure after its dependency overrides are resolved.
- Keep expected diagnostics in deterministic, reviewable baseline files.
- Publish actual reports when validation fails so maintainers can inspect and intentionally update baselines.
- Pin validation tooling and constrain tool installation to NuGet.org.

## Non-Goals

- Compare the public API surface with the latest package published to NuGet.org.
- Add validation to pipelines that do not produce the OSS NuGet artifact.
- Validate standalone utilities that are not independently deployed to production.
- Treat the OSS result as sufficient protection against dependency versions selected downstream by fhir-paas.
- Automatically accept or update changed baselines.
- Refactor the existing build, test, or packaging stages beyond the shared packaging path.

## Existing Build Flow

Both `build/pr-pipeline.yml` and `build/ci-pipeline.yml` run a `BuildArtifacts` stage whose Linux packaging job invokes `build/jobs/build.yml` with `packageArtifacts: true`. That template invokes `build/jobs/package.yml`, which:

1. Packages the web applications.
2. Runs `dotnet pack` for the repository and writes packages to `$(Build.ArtifactStagingDirectory)/nupkgs`.
3. Publishes the NuGet directory as the `nuget` build artifact.

The validation belongs between steps 2 and 3. Placing it in the shared package template gives PR and CI identical behavior without pipeline-specific duplication.

## Considered Approaches

### 1. Shared post-pack validation template

Run one reusable validation step after `dotnet pack` and before artifact publication. The validator selects supported Web packages and target frameworks, restores each package into an isolated consumer project, publishes the closure, and validates it.

**Advantages**

- Validates the actual package artifact and its dependency metadata.
- Covers PR and CI through one shared integration point.
- Prevents invalid NuGet artifacts from being published by the build.
- Keeps Azure Pipelines orchestration separate from validation logic.
- Can be reproduced locally against a package directory.

**Disadvantages**

- Adds restore and publish work for every supported Web package/framework pair.
- Requires deterministic temporary source configuration and cleanup.

### 2. Separate validation stage

Publish the NuGet artifact, download it in another job, and validate it there.

**Advantages**

- Isolates the validation tool and workload from packaging.
- Could run independently after packaging.

**Disadvantages**

- Publishes an invalid artifact before validation completes.
- Adds artifact upload/download latency.
- Requires additional stage dependencies and wiring in both pipelines.
- Makes local reproduction less direct.

### 3. MSBuild-integrated validation

Attach validation targets to each packable project or the repository pack operation.

**Advantages**

- Makes the check visible during local `dotnet pack`.
- Associates failures closely with individual projects.

**Disadvantages**

- Couples a repository policy tool to every project build.
- Complicates tool bootstrapping, report aggregation, and baseline paths in MSBuild.
- Risks changing developer pack behavior unrelated to CI artifact production.

## Decision

Use the shared post-pack validation template for four deployment-root packages:

- `Microsoft.Health.Fhir.Stu3.Web`
- `Microsoft.Health.Fhir.R4.Web`
- `Microsoft.Health.Fhir.R4B.Web`
- `Microsoft.Health.Fhir.R5.Web`

Each Web package references the shared API, Azure infrastructure, Cosmos DB, SQL Server, authentication, and task-management components. Restoring a Web package therefore validates the complete server runtime graph for both storage implementations. Storage configuration does not produce a distinct assembly closure, so separate SQL Server and Cosmos DB consumers would duplicate the same check.

Validate each package for `net8.0` and `net9.0`, producing eight OSS closures. Add another deployment root only when a standalone utility is independently deployed to production.

The fhir-paas repository must separately validate the exact final publish directory for every deployed FHIR composition after all downstream package pins, central versions, runtime identifiers, and overrides have been applied. This consumer-side check is authoritative because OSS cannot observe dependency selections made downstream.

## Components

### Pipeline step template

Add `build/steps/validate-nuget-binary-closure.yml`. It will:

- Accept package, baseline, report, and temporary-work directories as parameters.
- Install a pinned `checkbinarycompat` version using a temporary NuGet configuration containing only NuGet.org.
- Invoke the repository validation script.
- Publish the report directory as a diagnostic build artifact under `succeededOrFailed()`.

The tool version is declared once in the template and must be changed through normal code review.

### Validation script

Add a cross-platform PowerShell script under `build/scripts/`. Keeping package inspection and consumer-project generation outside inline YAML makes the behavior locally reproducible and keeps the template readable.

The script will:

1. Enumerate `.nupkg` files in ordinal order and exclude `.snupkg` files.
2. Read package ID, version, and managed target frameworks from package metadata and archive paths rather than parsing file names.
3. Select the four required Web package IDs and reject a missing, duplicate, or unsupported deployment root.
4. For every selected package/framework pair, create an isolated temporary SDK consumer application.
5. Generate a temporary NuGet configuration with:
   - The just-built package directory as the first source.
   - The repository's existing public NuGet sources.
   - Package-source mappings that allow the generated package to resolve from the local source.
6. Add an exact-version `PackageReference` to the generated package.
7. Restore and publish the consumer application.
8. Run `checkbinarycompat` against the publish directory using:
   - A package-and-framework-specific checked-in baseline.
   - A separate actual report path.
   - Assembly-list and summary/new-warning output.
   - Framework-assembly handling appropriate for SDK-style applications.
9. Aggregate failures and return a nonzero exit code after all selected package/framework pairs have produced reports.
10. Remove temporary consumer and restore directories in a `finally` block while preserving reports.

All command failures are surfaced. The script will not silently skip a selected package, supported framework, missing baseline, or malformed selected package.

### Baselines

Store baselines under `build/binarycompat/`. Use a deterministic file name based on a filesystem-safe package ID and target framework:

```text
build/binarycompat/<PackageId>.<TargetFramework>.txt
```

Each file is the sorted `checkbinarycompat` diagnostic report for that consumer closure. An empty expected report is represented by a checked-in empty file. A baseline is required for every selected Web package/framework pair; missing and orphaned baselines are failures so deployment-root and target-framework changes are explicit in review. The expected OSS inventory is eight files.

### Reports

Write actual output under the build artifact staging directory:

```text
binarycompat/<PackageId>/<TargetFramework>/
```

Each directory contains the actual compatibility report, analyzed assembly list, and concise comparison output. The pipeline publishes this tree even when validation fails. It must not modify checked-in baselines in place.

## Data Flow

```mermaid
flowchart LR
    A[dotnet pack] --> B[nupkgs directory]
    B --> C[Select Web package and TFM pairs]
    C --> D[Generate temporary consumer project]
    D --> E[Restore exact local package]
    E --> F[Publish resolved closure]
    F --> G[Run pinned checkbinarycompat]
    H[Checked-in baseline] --> G
    G --> I[Actual reports artifact]
    G -->|all match| J[Publish NuGet artifact]
    G -->|drift or error| K[Fail packaging job]
```

The downstream consumer flow is:

```mermaid
flowchart LR
    A[fhir-paas dependency resolution] --> B[dotnet publish]
    B --> C[Exact application publish directory]
    C --> D[Run pinned checkbinarycompat]
    E[Checked-in fhir-paas baseline] --> D
    D -->|all match| F[Assemble and publish image]
    D -->|drift or error| G[Fail before image publication]
```

## Pipeline Integration

Update `build/jobs/package.yml` so the order is:

1. Package web applications.
2. Pack NuGet packages.
3. Validate NuGet binary closures.
4. Publish deployment artifacts.
5. Publish the NuGet artifact.

No direct changes are required in `build/pr-pipeline.yml` or `build/ci-pipeline.yml`; both already consume the shared packaging path. The implementation will nevertheless verify both call chains so a future parameter or condition cannot bypass the validator.

## Error Handling

The following conditions fail the packaging job:

- A required Web package was not generated.
- A selected Web package is malformed or has no discoverable supported managed target framework.
- Two archives declare the same required Web package ID and version.
- A package cannot be restored from the local output.
- Restore resolves a different version than the exact generated package version.
- Consumer publish fails.
- A required baseline is absent.
- A checked-in baseline has no corresponding generated package/framework pair.
- `checkbinarycompat` reports output different from the baseline.
- Tool installation or execution fails.

Validation continues across independent selected package/framework pairs when possible so one run produces a complete diagnostic artifact. The final exit code remains failing if any pair failed.

## Determinism and Security

- Pin `checkbinarycompat`; do not use an unbounded latest version.
- Install the tool through a temporary NuGet.org-only configuration.
- Reference generated packages by exact ID and version.
- Put the local package source first and verify the restored package path/version from restore assets.
- Sort packages, frameworks, diagnostics, and baseline inventory comparisons ordinally.
- Use isolated work and package-cache directories to avoid agent cache contamination.
- Do not send package contents or build metadata to services beyond the repository's existing package sources.

## Testing and Validation

### Script behavior

Exercise the script against a small temporary package set to verify:

- Non-symbol package discovery and `.snupkg` exclusion.
- Package identity and target-framework discovery.
- Selection of exactly the four required Web package IDs.
- Failure when a required Web package is missing.
- Exact local package restore.
- Independent `net8.0` and `net9.0` report paths.
- Success when reports match baselines.
- Failure with a preserved actual report when a baseline differs.
- Failure for missing and orphaned baselines.
- Failure for malformed or duplicate packages.
- Failure when code compiled against a fixture dependency containing `OldMethod` is published with a dependency version where that member was removed or renamed.
- Success when the final resolved dependency update remains binary compatible.

Tests should use the repository's existing test tooling where available; no new test framework will be introduced solely for the build script.

### Pipeline configuration

- Parse all changed YAML files.
- Confirm both PR and CI `BuildArtifacts` call chains reach the validation template exactly once.
- Run the validator against packages produced from the current repository.
- Confirm the normal NuGet artifact is not published after a validation failure.
- Confirm the binary compatibility report artifact is published on success and failure.

## Baseline Maintenance

The initial change will generate baselines from the current repository package output. Later dependency or packaging changes may alter a closure. Maintainers must inspect the published actual report, determine that the change is intentional and safe, and replace the corresponding checked-in baseline in the same PR. Baseline updates are never generated or committed automatically by CI.

The fhir-paas repository owns a separate baseline for each final deployed application closure. Those baselines are generated from the same Linux and runtime-identifier environment used to create the deployment image. A downstream dependency pin that introduces a new unresolved member must fail before image publication even if the OSS producer baselines still match.

## Expected Outcome

PR and CI builds reject supported OSS Web package closures that differ from reviewed expectations. The reduced OSS matrix validates eight closures rather than every library package. The authoritative fhir-paas gate rejects final application closures broken by downstream dependency pins before an image can be published. Both gates produce actionable reports and make accepted closure changes explicit without changing runtime or FHIR behavior.

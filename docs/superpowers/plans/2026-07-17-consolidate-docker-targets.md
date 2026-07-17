# Consolidate Docker Targets and Retire .NET 8 Support Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Incorporate Docker image updates from PRs #5677 and #5678 while removing all .NET 8 compilation, CI, and compatibility build surfaces.

**Architecture:** The repository will be a .NET 10-only build. Shared MSBuild configuration defines only `net10.0`; pipeline definitions retain their .NET 10 jobs and remove .NET 8-specific jobs or framework arguments. The Dockerfile continues its existing multi-stage image flow with the requested SDK and ASP.NET runtime image tags.

**Tech Stack:** .NET 10, MSBuild, Azure Pipelines YAML, Docker.

---

## File Structure

| File | Responsibility |
| --- | --- |
| `build/docker/Dockerfile` | Defines the SDK build and ASP.NET runtime images for published FHIR server containers. |
| `Directory.Build.props` | Defines repository-wide target frameworks for SDK-style projects. |
| `Directory.Packages.props` | Selects the framework-specific ASP.NET package version. |
| `build/ci-pipeline.yml` | Runs continuous-integration tests and Docker builds. |
| `build/pr-pipeline.yml` | Runs pull-request tests and Docker builds. |
| `build/.vsts-PRInternalChecks-azureBuild-pipeline.yml` | Defines the internal PR validation build command. |
| `build/dotnet8-compat/global.json` | Obsolete .NET 8 compatibility SDK pin; deleted. |

### Task 1: Establish Configuration Regression Checks

**Files:**
- Verify: `build/docker/Dockerfile`
- Verify: `Directory.Build.props`
- Verify: `Directory.Packages.props`
- Verify: `build/ci-pipeline.yml`
- Verify: `build/pr-pipeline.yml`
- Verify: `build/.vsts-PRInternalChecks-azureBuild-pipeline.yml`
- Verify: `build/dotnet8-compat/global.json`

- [ ] **Step 1: Run the pre-change configuration checks**

Run:

```powershell
$paths = @(
  'build/docker/Dockerfile',
  'Directory.Build.props',
  'Directory.Packages.props',
  'build/ci-pipeline.yml',
  'build/pr-pipeline.yml',
  'build/.vsts-PRInternalChecks-azureBuild-pipeline.yml',
  'build/dotnet8-compat/global.json'
)
Select-String -Path $paths -Pattern 'net8\.0|8\.0\.422|10\.0\.301-azurelinux3\.0|10\.0\.9-azurelinux3\.0'
```

Expected: Matches identify the .NET 8 build targets and the two pre-update Docker image tags.

- [ ] **Step 2: Record that the configuration check fails the desired final state**

Run:

```powershell
if (Test-Path 'build/dotnet8-compat/global.json') { throw 'Obsolete .NET 8 compatibility SDK configuration remains.' }
```

Expected: FAIL with `Obsolete .NET 8 compatibility SDK configuration remains.`

### Task 2: Update Docker Images and Retire .NET 8 Build Targets

**Files:**
- Modify: `build/docker/Dockerfile:2,84`
- Modify: `Directory.Build.props:23`
- Modify: `Directory.Packages.props:12-15`
- Modify: `build/ci-pipeline.yml:94-104`
- Modify: `build/pr-pipeline.yml:54-64`
- Modify: `build/.vsts-PRInternalChecks-azureBuild-pipeline.yml:35`
- Delete: `build/dotnet8-compat/global.json`

- [ ] **Step 1: Update the Docker SDK and runtime tags**

In `build/docker/Dockerfile`, replace the two image declarations with:

```dockerfile
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.302-azurelinux3.0 AS build
```

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0.10-azurelinux3.0 AS runtime
```

- [ ] **Step 2: Restrict repository builds to .NET 10**

In `Directory.Build.props`, replace the shared framework list with:

```xml
<TargetFrameworks>net10.0</TargetFrameworks>
```

- [ ] **Step 3: Remove the .NET 8 CI jobs**

In `Directory.Packages.props`, remove the .NET 8 package-version branch:

```xml
<When Condition="'$(TargetFramework)' == 'net8.0'">
  <PropertyGroup>
    <AspNetPackageVersion>8.0.26</AspNetPackageVersion>
  </PropertyGroup>
</When>
```

The existing `net9.0` and `net10.0` branches remain unchanged.

- [ ] **Step 4: Remove the .NET 8 CI jobs**

In both `build/ci-pipeline.yml` and `build/pr-pipeline.yml`, remove the complete `Linux_dotnet8` job:

```yaml
- job: Linux_dotnet8
  pool:
    name: '$(InternalPool)'
  demands:
    - ImageOverride -equals $(InternalLinuxImage)
  variables:
    AllowPtrToDetectTestRunRetryFiles: true
  steps:
  - template: ./jobs/build.yml
    parameters:
      targetBuildFramework: 'net8.0'
```

Retain the adjacent `Windows_dotnet10` job unchanged so the build-and-test stage still runs under its configured default framework.

- [ ] **Step 5: Remove the internal .NET 8 framework override**

In `build/.vsts-PRInternalChecks-azureBuild-pipeline.yml`, change the build task argument to:

```yaml
arguments: --configuration ${{ parameters.BuildConfiguration }} --version-suffix $(build.buildnumber) /warnaserror
```

- [ ] **Step 6: Delete obsolete compatibility configuration**

Delete `build/dotnet8-compat/global.json`. The file is the only member of the .NET 8 compatibility directory, so remove the empty directory from the working tree as part of the deletion.

- [ ] **Step 7: Inspect the resulting diff**

Run:

```powershell
git diff --check
git diff -- build/docker/Dockerfile Directory.Build.props Directory.Packages.props build/ci-pipeline.yml build/pr-pipeline.yml build/.vsts-PRInternalChecks-azureBuild-pipeline.yml build/dotnet8-compat/global.json
```

Expected: The diff contains only the two Docker tag upgrades and removal of .NET 8 targets, jobs, and compatibility configuration.

### Task 3: Validate the .NET 10-Only Build Configuration

**Files:**
- Verify: `build/docker/Dockerfile`
- Verify: `Directory.Build.props`
- Verify: `Directory.Packages.props`
- Verify: `build/ci-pipeline.yml`
- Verify: `build/pr-pipeline.yml`
- Verify: `build/.vsts-PRInternalChecks-azureBuild-pipeline.yml`
- Verify absence: `build/dotnet8-compat/global.json`

- [ ] **Step 1: Verify required Docker image tags**

Run:

```powershell
Select-String -Path build/docker/Dockerfile -Pattern 'mcr\.microsoft\.com/dotnet/sdk:10\.0\.302-azurelinux3\.0','mcr\.microsoft\.com/dotnet/aspnet:10\.0\.10-azurelinux3\.0'
```

Expected: Exactly two matches, one SDK image and one ASP.NET runtime image.

- [ ] **Step 2: Verify .NET 8 build configuration is absent**

Run:

```powershell
$paths = @(
  'build/docker/Dockerfile',
  'Directory.Build.props',
  'Directory.Packages.props',
  'build/ci-pipeline.yml',
  'build/pr-pipeline.yml',
  'build/.vsts-PRInternalChecks-azureBuild-pipeline.yml'
)
$net8References = Select-String -Path $paths -Pattern 'net8\.0|dotnet8'
if ($net8References) {
  $net8References | Format-Table -AutoSize
  throw '.NET 8 build configuration remains.'
}
if (Test-Path 'build/dotnet8-compat') {
  throw 'Obsolete .NET 8 compatibility directory remains.'
}
```

Expected: No output and exit code 0.

- [ ] **Step 3: Build the solution with the repository SDK**

Run:

```powershell
dotnet build --configuration Release --no-restore
```

Expected: Build succeeds with the .NET 10 SDK selected by `global.json`.

- [ ] **Step 4: Commit the implementation**

Run:

```powershell
git add build/docker/Dockerfile Directory.Build.props Directory.Packages.props build/ci-pipeline.yml build/pr-pipeline.yml build/.vsts-PRInternalChecks-azureBuild-pipeline.yml build/dotnet8-compat/global.json
git commit -m "Retire .NET 8 build targets" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: 960d034a-d692-44b1-b0b2-8935f4ced9f8"
```

Expected: One commit containing the Docker image updates and .NET 8 build-target retirement.

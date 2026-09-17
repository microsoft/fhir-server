# Dependabot discovery project

This project is not built, shipped, tested or referenced by anything. It exists so that
Dependabot's NuGet updater has one cheap project to look at instead of the whole repository.

## Why it exists

Dependabot runs a **full dependency discovery pass before every group** declared in
`.github/dependabot.yml`. With `directory: "/"` each of those passes walked
`Microsoft.Health.Fhir.sln` and all 78 projects.

Measured on the timed-out job:

| | `directory: "/"` | this project |
| --- | --- | --- |
| Projects per discovery pass | 78 | 1 |
| Discovery time per group | ~4 min | ~2 sec |
| Time lost to re-discovery | ~25 min of 55 | ~20 sec |
| Restores | 1,023 | 509 |
| Outcome | **timed out at 55 min** | **completed** |

GitHub's 55 minute limit for Dependabot jobs is fixed and cannot be raised. Discovery alone
consumed 46% of it, so the job died partway through and the later groups - including the
catch-all `minor-and-patch` group - never ran. That is why routine updates such as
NSubstitute 6.0.0 -> 6.2.0 were never proposed.

## How it works

`.github/dependabot.yml` points the `nuget` ecosystem at this directory. Dependabot's entry
point search only looks at files directly in the configured directory, so the root solution is
never found and never expanded.

Central Package Management still applies: every `PackageReference` here is version-less and
`Directory.Packages.props` at the repository root remains the single source of truth. That is
also the file Dependabot edits, so the resulting pull requests are unchanged in shape.

## Rules

* **Never add this project to `Microsoft.Health.Fhir.sln`.** Dependabot walks upward from the
  configured directory to any solution that claims the project, and being claimed would silently
  restore the slow whole-repository behaviour with no visible error.
* **Keep `TargetFramework` in `Directory.Build.props`, not in the `.csproj`.**
  `Directory.Packages.props` selects `AspNetPackageVersion` with a `<Choose>` on
  `$(TargetFramework)`, and it is imported before the project body is evaluated.
* **Keep the `PackageReference` list in step with `Directory.Packages.props.`** A package that is
  missing here is a package Dependabot stops updating, silently.

## Keeping it in sync

Nothing to run by hand. The `ValidateDependabotCoverage` target in the `.csproj` compares its
`PackageReference` list against the `PackageVersion` list in `Directory.Packages.props` and fails
on any difference in either direction, so a plain restore is the whole check:

```bash
dotnet restore src/DependabotDiscovery/DependabotDiscovery.csproj
```

The PR pipeline runs exactly that in its `ValidateDependabotDiscovery` stage. When it fails, add
or remove the named `PackageReference` entries to match `Directory.Packages.props`.

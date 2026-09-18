# Dependabot discovery project

A stub project listing every NuGet package this repository uses. Nothing builds it, ships it or
references it. It exists so Dependabot has one small project to look at instead of all 78.

## Why it exists

Dependabot rescans the repository before *every* group in `.github/dependabot.yml`. Pointed at the
repository root it opened all 78 projects each time, roughly four minutes per group. That burned
46% of the 55 minute limit GitHub puts on Dependabot jobs, so the job timed out partway through
and the last groups never ran at all. That is why ordinary updates quietly stopped appearing.

Pointed here, a scan takes about two seconds. The job now finishes in roughly 40 minutes with
every group running.

Versions still come from `Directory.Packages.props`, and that is still the file Dependabot edits,
so the pull requests look exactly as they did before.

## If the PR build fails on this project

The build compares this project's package list against `Directory.Packages.props` and fails if
they differ. The error message names the packages. Add or remove the matching lines:

```xml
<PackageReference Include="Some.Package" />
```

No version numbers, and no script to run. To check before pushing:

```bash
dotnet restore build/DependabotDiscovery/DependabotDiscovery.csproj
```

## Three things not to do

- **Don't add this project to `Microsoft.Health.Fhir.sln`.** Dependabot searches upward for a
  solution that owns the project. If it finds one, it goes back to scanning everything, and
  nothing warns you.
- **Don't move `TargetFramework` into the `.csproj`.** `Directory.Packages.props` reads it to pick
  the ASP.NET version, and it does that before the `.csproj` is read. The ASP.NET packages would
  silently end up with no version.
- **Don't put versions here.** Central Package Management supplies them.

# Dependabot discovery project

A stub project listing every NuGet package this repository uses. Nothing builds it, ships it or
references it. It exists so Dependabot has one small project to look at instead of all of them.

## Why it exists

Dependabot rescans the repository before *every* group in `.github/dependabot.yml`. Pointed at the
repository root it opened all 78 projects each time, roughly four minutes per group, against the
fixed 55 minute limit GitHub puts on Dependabot jobs. The job timed out partway through and the
last groups never ran at all. That is why ordinary updates quietly stopped appearing.

Pointed here, a scan takes about two seconds and every group runs. The job now finishes with
roughly a quarter of an hour to spare - so if it ever starts timing out again, discovery is no
longer the thing to fix.

Versions still come from `Directory.Packages.props`, and that is still the file Dependabot edits.

## If the PR build fails on this project

The build checks four things: that this project's package list matches `Directory.Packages.props`,
that every package resolves to a real version, that the solution has not claimed this project, and
that `.github/dependabot.yml` still points here. The error says which one failed and what to do.

Usually it is the package list, and the fix is to add or remove a line:

```xml
<PackageReference Include="Some.Package" />
```

No version numbers, and no script to run. To check before pushing:

```bash
dotnet msbuild build/DependabotDiscovery -t:ValidateDependabotCoverage
```

That needs no package feeds and takes about a second.

## Three things not to do

Each of these used to break Dependabot silently. They fail the build now, but the reasoning is
still worth knowing.

- **Don't add this project to `Microsoft.Health.Fhir.sln`.** Dependabot searches upward for a
  solution that owns the project. If it finds one, it goes back to scanning everything.
- **Don't move `TargetFramework` into the `.csproj`.** `Directory.Packages.props` reads it to pick
  the ASP.NET version, and it does that before the `.csproj` is read. The ASP.NET packages would
  end up with no version at all.
- **Don't put versions here.** Central Package Management supplies them.

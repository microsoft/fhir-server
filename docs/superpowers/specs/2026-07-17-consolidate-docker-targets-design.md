# Consolidate Docker Targets and Retire .NET 8 Support

## Context

The Docker image base references need the SDK and ASP.NET runtime updates from PRs
#5678 and #5677. The repository also retains .NET 8 as a compile target and as
an active CI validation path, despite .NET 10 being the supported build target.

## Decision

Consolidate the two Docker updates in one change and retire .NET 8 support from
all active build surfaces:

- Update the Docker build image to `mcr.microsoft.com/dotnet/sdk:10.0.302-azurelinux3.0`.
- Update the Docker runtime image to `mcr.microsoft.com/dotnet/aspnet:10.0.10-azurelinux3.0`.
- Restrict the repository-wide target framework to `net10.0`.
- Remove Linux .NET 8 CI jobs from both continuous-integration pipeline definitions.
- Remove the explicit `net8.0` internal-checks build argument.
- Remove the obsolete `build/dotnet8-compat` SDK configuration.

## Behavior

Builds, unit tests, packaging, and Docker publication continue using .NET 10.
Docker image construction restores and publishes the selected FHIR web project
with the updated SDK image, then runs it from the updated ASP.NET runtime image.
There is no .NET 8 fallback target, compatibility job, or SDK pin.

## Failure Handling

The change intentionally lets existing build and restore failures surface through
the current pipeline tasks. No fallback SDK, conditional target selection, or
silent compatibility path is introduced.

## Validation

Validate that Docker references both requested image tags, no .NET 8 build target
or compatibility configuration remains, and the solution builds using the
repository's .NET 10 SDK.

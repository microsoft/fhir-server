---
mode: 'agent'
description: 'Update .NET SDK and runtime versions across the FHIR server repository to the latest stable patch versions within supported major versions'
---

# .NET Version Updater

Update .NET SDK and runtime versions across the Microsoft FHIR Server repository to the latest stable patch versions for the supported major versions (.NET 10 and .NET 11).

## Files to Update

### 1. `global.json`
- Update `sdk.version` to the latest stable patch for the chosen major version.
- Keep the latest SDK in `global.json` and the .NET 10 SDK in `build/dotnet10-compat/global.json`. Preserve the `test.runner` setting in the root file.

### 2. `build/docker/Dockerfile` (build stage)
- Update the SDK image tag in the `FROM` line.
- The Docker SDK image is maintained independently from `global.json` and may intentionally differ by patch.
- Keep the build stage on .NET 11 and retain the .NET 10 runtime stage used by the SQL build generators.

### 3. `build/docker/Dockerfile` (runtime stage)
- Update the ASP.NET runtime image tag to the latest compatible patch for the same major version.

## Version Guidance

- Use the latest stable patch for the selected supported major version.
- Keep both `net11.0` and `net10.0` in `Directory.Build.props`. Select framework-compatible dependency versions in `Directory.Packages.props`.
- Keep `defaultBuildFramework` on `net11.0`. Publish multi-target NuGet packages, but only .NET 11 web archives, integration test archives, and Docker images. Preserve artifact names and image tags.
- Do not require exact patch equality between `global.json` and the Docker SDK image.

## Release Metadata

Use Microsoft .NET release metadata to identify current SDK patches:

```bash
curl -s "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/11.0/releases.json" | python3 -c "import sys, json; data=json.load(sys.stdin); sdks=[r['sdk']['version'] for r in data['releases'] if 'sdk' in r]; print('\n'.join(sorted(set(sdks), reverse=True)[:5]))"

# .NET 10
curl -s "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json" | python3 -c "import sys, json; data=json.load(sys.stdin); sdks=[r['sdk']['version'] for r in data['releases'] if 'sdk' in r]; print('\n'.join(sorted(set(sdks), reverse=True)[:5]))"
```

## Docker Tag Checks

Verify the SDK and ASP.NET runtime images exist in MCR before updating:

```bash
curl -s -L "https://mcr.microsoft.com/v2/dotnet/sdk/tags/list" | grep -o '"11\.0\.[^"]*-azurelinux3\.0"'
curl -s -L "https://mcr.microsoft.com/v2/dotnet/aspnet/tags/list" | grep -o '"11\.0\.[^"]*-azurelinux3\.0"'

# .NET 10 images
curl -s -L "https://mcr.microsoft.com/v2/dotnet/sdk/tags/list" | grep -o '"10\.0\.[^"]*-azurelinux3\.0"'
curl -s -L "https://mcr.microsoft.com/v2/dotnet/aspnet/tags/list" | grep -o '"10\.0\.[^"]*-azurelinux3\.0"'
```

## Process

1. Check current versions in `global.json` and `build/docker/Dockerfile`.
2. Review release metadata for the selected supported major version.
3. Verify that the selected Docker SDK and runtime tags exist in MCR and use the `-azurelinux3.0` suffix.
4. Update the SDK and runtime versions independently where required.
5. Validate that the versions are supported and the image tags are available.

## Validation

- Confirm `global.json` and the Docker SDK image are on the intended major version.
- Do not enforce exact SDK patch matching; patch drift is allowed when intentional.
- Check for stale version references after the update.

## Examples

For the .NET 10 SQL generator runtime:

```dockerfile
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/runtime:10.0.14-azurelinux3.0 AS sql-tools-runtime
```

For the current .NET 11 configuration:

```json
// global.json
{
  "sdk": {
    "version": "11.0.100"
  },
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

```dockerfile
// build/docker/Dockerfile
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:11.0.100-azurelinux3.0 AS build
FROM mcr.microsoft.com/dotnet/aspnet:11.0.0-azurelinux3.0 AS runtime
```

## Summary

Update the supported .NET 10 and .NET 11 SDK and runtime patches. Verify the Docker tags, preserve both CI test jobs, and keep the existing deployment artifact contract.

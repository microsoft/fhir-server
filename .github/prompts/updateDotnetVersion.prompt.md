---
mode: 'agent'
description: 'Update .NET SDK and runtime versions across the FHIR server repository to the latest supported release for each major version'
---

# .NET Version Updater

Update .NET SDK and runtime versions across the Microsoft FHIR Server repository to the latest supported release for each major version (.NET 10 and .NET 11).

## Files to Update

### 1. `global.json`
- Update `sdk.version` to the latest stable patch for the chosen major version.

### 2. `build/docker/Dockerfile` (build stage)
- Update the SDK image tag in the `FROM` line.
- The Docker SDK image is maintained independently from `global.json` and may intentionally differ by patch.
- Example: `global.json` can pin `10.0.301` while the Docker SDK image uses `10.0.302-azurelinux3.0`.

### 3. `build/docker/Dockerfile` (runtime stage)
- Update the ASP.NET runtime image tag to the latest compatible patch for the same major version.

## Version Guidance

- Use the latest stable patch for a generally available major version.
- Use the latest go-live release for a pre-GA major version.
- Keep guidance for .NET 10 and .NET 11 while both majors are supported.
- Do not require exact patch equality between `global.json` and the Docker SDK image.

## Release Metadata

Use Microsoft .NET release metadata to identify current SDK patches:

```bash
# .NET 10
curl -s "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json" | python3 -c "import sys, json; data=json.load(sys.stdin); sdks=[r['sdk']['version'] for r in data['releases'] if 'sdk' in r]; print('\n'.join(sorted(set(sdks), reverse=True)[:5]))"

# .NET 11
curl -s "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/11.0/releases.json" | python3 -c "import sys, json; data=json.load(sys.stdin); sdks=[r['sdk']['version'] for r in data['releases'] if 'sdk' in r]; print('\n'.join(sorted(set(sdks), reverse=True)[:5]))"
```

## Docker Tag Checks

Verify the SDK and ASP.NET runtime images exist in MCR before updating:

```bash
# .NET 10 images
curl -s -L "https://mcr.microsoft.com/v2/dotnet/sdk/tags/list" | rg -o '"10\.0\.[^"]*-azurelinux3\.0"'
curl -s -L "https://mcr.microsoft.com/v2/dotnet/aspnet/tags/list" | rg -o '"10\.0\.[^"]*-azurelinux3\.0"'

# .NET 11 images
curl -s -L "https://mcr.microsoft.com/v2/dotnet/sdk/tags/list" | rg -o '"11\.0\.[^"]*-azurelinux3\.0"'
curl -s -L "https://mcr.microsoft.com/v2/dotnet/aspnet/tags/list" | rg -o '"11\.0\.[^"]*-azurelinux3\.0"'
```

## Process

1. Check the SDK version in `global.json` and both image tags in `build/docker/Dockerfile`.
2. Review release metadata for the selected supported major version.
3. Verify that the selected Docker SDK and runtime tags exist in MCR and use the `-azurelinux3.0` suffix.
4. Update the SDK and runtime versions independently where required.
5. Validate that the versions are supported and the image tags are available.

## Validation

- Confirm `global.json` and both Docker images use the intended major version.
- Do not enforce exact SDK patch matching; patch drift is allowed when intentional.
- Check for stale version references after the update.

## Examples

For the current .NET 11 RC3 configuration:

```json
// global.json
{
  "sdk": {
    "version": "11.0.100-rc.3.26522.148",
    "allowPrerelease": true
  }
}
```

```dockerfile
// build/docker/Dockerfile
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:11.0.100-rc.3-azurelinux3.0 AS build
FROM mcr.microsoft.com/dotnet/aspnet:11.0.0-rc.3-azurelinux3.0 AS runtime
```

## Summary

Update the supported .NET 10 or .NET 11 SDK/runtime versions, verify the matching-major Docker tags, and allow for different patch formats between `global.json` and the Docker SDK image.

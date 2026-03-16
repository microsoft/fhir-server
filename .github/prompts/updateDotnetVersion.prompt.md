---
mode: 'agent'
description: 'Update .NET SDK and runtime versions across the FHIR server repository to the latest patch versions within the same major version'

---

# .NET Version Updater

Your goal is to update .NET SDK and runtime versions across the Microsoft FHIR Server repository to the latest stable patch versions while staying within the same major version (e.g., update .NET 9.x.x versions but don't change from .NET 9 to .NET 10).

## Files to Update

When updating .NET versions, you need to update the following files consistently:

### 1. Main .NET 9 SDK Configuration
- **File**: `global.json`
- **What to update**: The SDK version under `sdk.version`
- **Example**: `"version": "9.0.310"` → `"version": "9.0.xxx"` (latest patch)

### 2. Docker Build Image (SDK)
- **File**: `build/docker/Dockerfile`
- **Line**: The `FROM` statement for the build stage (line ~2)
- **What to update**: The SDK version in the Docker base image
- **Example**: `FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0.310-azurelinux3.0 AS build`
- **Note**: The SDK version must match the version in `global.json`

### 3. Docker Runtime Image (ASP.NET)
- **File**: `build/docker/Dockerfile`
- **Line**: The `FROM` statement for the runtime stage (line ~83)
- **What to update**: The ASP.NET runtime version
- **Example**: `FROM mcr.microsoft.com/dotnet/aspnet:9.0.12-azurelinux3.0 AS runtime`
- **Note**: Update to the latest compatible runtime version for the SDK major version

### 4. .NET 8 Compatibility SDK
- **File**: `build/dotnet8-compat/global.json`
- **What to update**: The SDK version under `sdk.version`
- **Example**: `"version": "8.0.417"` → `"version": "8.0.xxx"` (latest .NET 8 patch)
- **Note**: This is used for building .NET 8 target framework projects

## Process

### Step 1: Determine Current Versions
1. Check current SDK version in `global.json`
2. Check current Docker SDK version in `build/docker/Dockerfile`
3. Check current runtime version in `build/docker/Dockerfile`
4. Check current .NET 8 SDK version in `build/dotnet8-compat/global.json`

### Step 2: Find Latest Versions
Use the Microsoft .NET release metadata to find the latest patch versions:

**For .NET 9 (current main version):**
```bash
curl -s "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/9.0/releases.json" | python3 -c "import sys, json; data=json.load(sys.stdin); sdks=[r['sdk']['version'] for r in data['releases'] if 'sdk' in r]; print('\n'.join(sorted(set(sdks), reverse=True)[:5]))"
```

**For .NET 8 (compatibility):**
```bash
curl -s "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/8.0/releases.json" | python3 -c "import sys, json; data=json.load(sys.stdin); sdks=[r['sdk']['version'] for r in data['releases'] if 'sdk' in r]; print('\n'.join(sorted(set(sdks), reverse=True)[:5]))"
```

### Step 3: Verify Docker Images Exist
Before updating, verify that the Docker images are available in Microsoft Container Registry:

```bash
# Check SDK image
curl -s -L "https://mcr.microsoft.com/v2/dotnet/sdk/tags/list" | grep -o '"9.0.xxx[^"]*azurelinux'

# Check runtime image
curl -s -L "https://mcr.microsoft.com/v2/dotnet/aspnet/tags/list" | grep -o '"9.0.xx[^"]*azurelinux'
```

Look for images with the `-azurelinux3.0` suffix as that's what the Dockerfile uses.

### Step 4: Update Files

Update all four locations with the appropriate versions:

1. **global.json**: Latest .NET 9 SDK version
2. **build/docker/Dockerfile** (line ~2): SDK version matching global.json
3. **build/docker/Dockerfile** (line ~83): Latest .NET 9 runtime version
4. **build/dotnet8-compat/global.json**: Latest .NET 8 SDK version

### Step 5: Validate

After making changes:
1. Ensure `global.json` SDK version matches the Dockerfile SDK image version
2. Verify all versions are within the same major version (don't accidentally jump from 9.x to 10.x)
3. Check that no other files reference the old version numbers

```bash
# Check for old version references
grep -r "9.0.OLD_VERSION" . --include="*.json" --include="Dockerfile*" --include="*.yml"
```

## Important Rules

1. **Stay Within Major Version**: Only update patch versions (e.g., 9.0.310 → 9.0.315), never change major versions (e.g., 9.x → 10.x) without explicit approval
2. **Consistency**: The SDK version in `global.json` MUST match the Docker SDK image version
3. **Verify Availability**: Always verify that Docker images exist in MCR before updating
4. **Both .NET Versions**: Remember to update both .NET 9 (main) and .NET 8 (compat) configurations
5. **Runtime Independence**: The runtime version doesn't need to exactly match SDK version, but should be the latest compatible version

## Common Pitfalls

- ❌ Forgetting to update `build/dotnet8-compat/global.json`
- ❌ Updating SDK version but forgetting to update Docker image version
- ❌ Using an SDK version that doesn't have a corresponding `-azurelinux3.0` Docker image
- ❌ Accidentally changing major versions (9.x → 10.x)
- ❌ Not updating the runtime image version

## Example Update

If updating from .NET 9.0.310 to 9.0.315:

```json
// global.json
{
    "sdk": {
        "version": "9.0.315"  // Updated from 9.0.310
    }
}
```

```dockerfile
// build/docker/Dockerfile (line ~2)
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0.315-azurelinux3.0 AS build

// build/docker/Dockerfile (line ~83)
FROM mcr.microsoft.com/dotnet/aspnet:9.0.14-azurelinux3.0 AS runtime
```

```json
// build/dotnet8-compat/global.json
{
    "sdk": {
        "version": "8.0.420"  // Updated from 8.0.417
    }
}
```

## Testing

After updates, verify the changes don't break CI/CD:
1. The Docker build should succeed with the new SDK image
2. .NET 8 target framework builds should use the dotnet8-compat SDK
3. Runtime deployments should use the updated ASP.NET runtime image

## Summary

To update .NET to the latest patch version:
1. Find the latest SDK versions for .NET 9 and .NET 8
2. Update `global.json` (main SDK)
3. Update `build/docker/Dockerfile` (SDK and runtime images)
4. Update `build/dotnet8-compat/global.json` (.NET 8 SDK)
5. Verify all versions match and images exist in MCR

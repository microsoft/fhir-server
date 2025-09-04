# Network Security Perimeter Integration

## Summary
Updated the FHIR server deployment to associate storage accounts with Network Security Perimeter (NSP) for enhanced network security.

## Changes Made

### 1. Updated `samples/templates/default-azuredeploy-docker.json`
- Added two new parameters:
  - `networkSecurityPerimeterName`: Name of the NSP (default: "fhirbuildsnsp")
  - `networkSecurityPerimeterProfileName`: NSP profile name (default: "defaultProfile")
- Added new resource: `Microsoft.Storage/storageAccounts/networkSecurityPerimeterConfigurations`
  - Associates storage accounts with the specified NSP and profile
  - Only deployed when storage accounts are enabled (`enableIntegrationStore` condition)
  - Uses API version `2023-05-01` for better compatibility

### 2. Updated `build/jobs/provision-deploy.yml`
- Added NSP parameters to the template parameter object:
  - `networkSecurityPerimeterName = "fhirbuildsnsp"`
  - `networkSecurityPerimeterProfileName = "defaultProfile"`

## How It Works
1. When `enableExport` or `enableImport` is set to `true`, a storage account is created
2. The NSP configuration resource is then deployed, associating the storage account with the specified Network Security Perimeter
3. This provides network-level security controls for the storage account access

## Prerequisites
- The Network Security Perimeter named "fhirbuildsnsp" must exist in the subscription
- The "defaultProfile" profile must be configured in the NSP
- Appropriate permissions to create NSP associations

## Testing
The changes will be applied automatically during PR pipeline runs when storage accounts are created for FHIR deployments with export/import capabilities enabled.

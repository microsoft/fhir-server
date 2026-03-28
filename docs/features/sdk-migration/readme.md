# Feature: SDK Migration - Firely to Ignixa

## Problem Statement

The Microsoft FHIR Server currently depends heavily on the Firely SDK (v5.11.4) for:
- FHIR resource model types (version-specific assemblies)
- JSON/XML serialization/deserialization
- FhirPath expression compilation and evaluation
- Profile validation
- Search parameter extraction
- Terminology operations

This architecture requires **separate assemblies per FHIR version** (STU3, R4, R4B, R5), leading to:
- Complex build configurations with conditional compilation
- Deployment complexity with multiple DLLs
- Code duplication across version-specific projects
- Maintenance overhead for version-specific bug fixes

## Desired Outcome

A unified FHIR server using the Ignixa SDK that:
- Handles all FHIR versions (STU3, R4, R4B, R5, R6) in a single assembly
- Uses HTTP header-based version negotiation (FHIR spec compliant)
- Provides high-performance JSON serialization without Firely overhead
- Maintains full FHIR compliance for all operations

## Current State

| Metric | Value |
|--------|-------|
| Firely SDK version | 5.11.4 |
| Files using `Hl7.Fhir.*` | 400+ |
| Total Firely usages | 602+ |
| Version-specific projects | 16 (4 versions x 4 layers) |

## Investigations

| Investigation | Status | Verdict |
|--------------|--------|---------|
| [Complete Firely Replacement with Ignixa](investigations/complete-ignixa-replacement.md) | In Progress | Pending |
| [Ignixa FHIRPath Provider Migration](investigations/ignixa-fhirpath-provider.md) | In Progress | Recommended |

## Related ADRs

*None yet - pending investigation outcomes*

## Notes

- E2E tests are excluded from this migration scope
- The ignixa/ folder contains reference implementation examples
- Ignixa NuGet packages are available at https://www.nuget.org/packages?q=Ignixa

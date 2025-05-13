# Type Comparison in FHIR Server

## Overview

This document explains the purpose and usage of the compile-time conditional for resource type comparisons in the FHIR Server.

## Background

A performance issue was identified in the FHIR Server related to how resource types are compared. The issue arose from changes made to use string-based equality checks (`ResourceType.X.EqualsString(r.Type.ToString())`) instead of direct type equality checks (`r.Type == ResourceType.X`).

## Implementation Details

To address this issue, we've implemented a compile-time conditional that allows toggling between the two comparison methods:

1. **Legacy Method**: Direct type comparison using `==` operator
2. **Current Method**: String-based comparison using the `EqualsString` method

The conditional is controlled by the `USE_LEGACY_TYPE_COMPARISON` flag defined in the project build settings.

### How it Works

The `CompareResourceType` extension method in `ResourceTypeExtensions.cs` provides a single interface for type comparisons that uses the appropriate implementation based on the compile-time flag:

```csharp
public static bool CompareResourceType(this ResourceType resourceType, object other)
{
#if USE_LEGACY_TYPE_COMPARISON
    // Legacy implementation: direct type comparison
    if (other is ResourceType otherType)
    {
        return resourceType == otherType;
    }
    return false;
#else
    // Current implementation: string comparison
    return resourceType.EqualsString(other?.ToString());
#endif
}
```

Throughout the codebase, comparisons use either this method or direct conditional compilation depending on the context.

### HL7 FHIR Package Versions

When the `USE_LEGACY_TYPE_COMPARISON` flag is enabled, the solution also uses an older version of the HL7 FHIR packages (version 4.3.0) that is compatible with the legacy type comparison approach. This ensures complete compatibility with the older code behavior.

| Configuration | HL7 FHIR Package Version |
|---------------|--------------------------|
| Legacy Mode   | 4.3.0                    |
| Current Mode  | 5.11.4                   |

Additionally, the legacy validation packages (`Hl7.Fhir.Validation.Legacy.*`) are only included in the build when the `USE_LEGACY_TYPE_COMPARISON` flag is enabled, as they are specific to the legacy mode.

## Configuration

The `USE_LEGACY_TYPE_COMPARISON` flag is defined in the `Directory.Build.props` file:

```xml
<PropertyGroup>
    <!-- Other properties -->
    <DefineConstants>USE_LEGACY_TYPE_COMPARISON;$(DefineConstants)</DefineConstants>
</PropertyGroup>
```

### Toggling Between Implementations

To switch implementations:

1. **Enable Legacy Comparison**: 
   - Ensure `USE_LEGACY_TYPE_COMPARISON` is defined in the constants
   - This will automatically use HL7 FHIR package version 4.3.0

2. **Disable Legacy Comparison**: 
   - Remove `USE_LEGACY_TYPE_COMPARISON` from the constants
   - This will use the current HL7 FHIR package version 5.11.4

## Performance Considerations

The direct type comparison (`==`) is generally more efficient than string-based comparison, as it avoids string allocations and comparisons. However, the specific performance characteristics may vary depending on the usage patterns.

## Testing

When changing the comparison method, thorough testing should be performed to ensure proper functionality and to measure performance differences. Since switching implementations also changes the HL7 FHIR package version, testing should include verification of API compatibility and resource parsing.

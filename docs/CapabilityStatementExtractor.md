# Capability Statement Extractor

## Overview

The `CapabilityStatementExtractor` provides a simple way to extract resource types, search parameters, and search parameter types from a FHIR capability statement.

## Usage

### Extract All Resource Types

```csharp
var extractor = new CapabilityStatementExtractor();
IEnumerable<string> resourceTypes = extractor.GetResourceTypes(capabilityStatement);

foreach (var resourceType in resourceTypes)
{
    Console.WriteLine($"Resource: {resourceType}");
}
```

### Extract Search Parameters for a Specific Resource

```csharp
var extractor = new CapabilityStatementExtractor();
var searchParams = extractor.GetSearchParametersForResource(capabilityStatement, "Patient");

foreach (var param in searchParams)
{
    Console.WriteLine($"Parameter: {param.Name}");
    Console.WriteLine($"  Type: {param.Type}");
    Console.WriteLine($"  Definition: {param.Definition}");
    Console.WriteLine($"  Documentation: {param.Documentation}");
}
```

### Extract All Search Parameters

```csharp
var extractor = new CapabilityStatementExtractor();
var allSearchParams = extractor.GetAllSearchParameters(capabilityStatement);

foreach (var kvp in allSearchParams)
{
    Console.WriteLine($"Resource: {kvp.Key}");
    foreach (var param in kvp.Value)
    {
        Console.WriteLine($"  - {param.Name} ({param.Type})");
    }
}
```

## Dependency Injection

The extractor implements `ICapabilityStatementExtractor` interface for easy dependency injection:

```csharp
services.AddSingleton<ICapabilityStatementExtractor, CapabilityStatementExtractor>();
```

## Search Parameter Info

The `SearchParameterInfo` class contains the following properties:

- **Name**: The name of the search parameter
- **Type**: The type of the search parameter (e.g., String, Number, Date, Token, Reference, Quantity, URI, Composite, Special)
- **Definition**: The URL defining the search parameter (optional)
- **Documentation**: Human-readable documentation for the search parameter (optional)

## Notes

- Resource type matching is case-insensitive
- Duplicate resource types are automatically filtered
- The extractor validates input parameters and throws `ArgumentNullException` or `ArgumentException` for invalid inputs

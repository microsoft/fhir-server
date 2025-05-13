# Using the Legacy Type Comparison Feature Flag

This feature flag (`USE_LEGACY_TYPE_COMPARISON`) was added to conditionally toggle between two implementations for resource type comparisons in the FHIR server:

1. A legacy approach using direct type equality checks (`r.Type == ResourceType.X`)
2. A current approach using string-based equality checks (`ResourceType.X.EqualsString(r.Type.ToString())`)

## How to Enable/Disable the Feature

The feature is controlled by the `UseLegacyTypeComparison` property in Directory.Build.props:

```xml
<PropertyGroup>
  <UseLegacyTypeComparison Condition="'$(UseLegacyTypeComparison)' == ''">true</UseLegacyTypeComparison>
  <DefineConstants Condition="'$(UseLegacyTypeComparison)' == 'true'">USE_LEGACY_TYPE_COMPARISON;$(DefineConstants)</DefineConstants>
  <DefineConstants Condition="'$(UseLegacyTypeComparison)' != 'true'">HAS_BRACKET_EXPRESSION;HAS_VERSION_INDEPENDENT_TYPES;$(DefineConstants)</DefineConstants>
</PropertyGroup>
```

You can override this setting when building:

```
dotnet build /p:UseLegacyTypeComparison=true
```

## Outstanding Issues

There are several files that need to be conditionally handled when switching between legacy and new code:

1. **ServerProvideProfileValidation.cs** - Needs conditional handling for ArtifactSummary types
2. **ExportAnonymizer.cs** and **ExportAnonymizerFactory.cs** - Needs conditional handling for the Anonymizer namespace
3. **ResourceTypeExtensions.cs** - Needs conditional handling for VersionIndependentResourceTypesAll
4. **ProfileValidator.cs** - Needs conditional handling for Validator class
5. **PerfTester/Program.cs** - Needs conditional handling for Bundle class

## Possible Solutions

1. Create stub implementations for the problematic classes that are conditionally included based on the feature flag.
2. Use full preprocessing directives (`#if USE_LEGACY_TYPE_COMPARISON` / `#else` / `#endif`) to include different implementations based on the flag.
3. Update the Directory.Packages.props to properly handle package versions for each mode.

## Next Steps

1. Finish implementing conditional code for the problematic files
2. Test both modes thoroughly to ensure functionality is preserved
3. Document the feature for future developers

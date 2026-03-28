// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlOnFhir.Materialization;

/// <summary>
/// Maps FHIR type names (from SQL on FHIR v2 ViewDefinition column types) to SQL Server column types.
/// </summary>
/// <remarks>
/// The FHIR type names come from <c>Ignixa.SqlOnFhir.Evaluation.ColumnSchema.Type</c>, which returns
/// FHIR primitive type names such as "string", "dateTime", "decimal", "boolean", "integer", "instant", etc.
/// When a type is not explicitly defined in the ViewDefinition, it may be inferred by the schema evaluator
/// or may be null/empty, in which case we fall back to <c>nvarchar(max)</c>.
/// </remarks>
public static class FhirTypeToSqlTypeMap
{
    /// <summary>
    /// The default SQL type used when the FHIR type is unknown or not specified.
    /// </summary>
    public const string DefaultSqlType = "nvarchar(max)";

    private static readonly Dictionary<string, string> TypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // FHIR primitive types → SQL Server types
        ["string"] = "nvarchar(max)",
        ["uri"] = "nvarchar(4000)",
        ["url"] = "nvarchar(4000)",
        ["canonical"] = "nvarchar(4000)",
        ["code"] = "nvarchar(256)",
        ["id"] = "nvarchar(128)",
        ["oid"] = "nvarchar(256)",
        ["uuid"] = "nvarchar(64)",
        ["markdown"] = "nvarchar(max)",
        ["base64Binary"] = "nvarchar(max)",

        // Numeric types
        ["boolean"] = "bit",
        ["integer"] = "int",
        ["integer64"] = "bigint",
        ["positiveInt"] = "int",
        ["unsignedInt"] = "int",
        ["decimal"] = "decimal(18, 9)",

        // Date/time types
        ["date"] = "date",
        ["dateTime"] = "datetime2(7)",
        ["instant"] = "datetime2(7)",
        ["time"] = "time(7)",

        // Composite FHIR types that may appear as column types
        // These are serialized as JSON strings when flattened into columns
        ["Quantity"] = "nvarchar(max)",
        ["Reference"] = "nvarchar(4000)",
        ["Coding"] = "nvarchar(max)",
        ["CodeableConcept"] = "nvarchar(max)",
        ["Period"] = "nvarchar(max)",
        ["Identifier"] = "nvarchar(max)",
    };

    /// <summary>
    /// Gets all supported FHIR type mappings.
    /// </summary>
    public static IReadOnlyDictionary<string, string> AllMappings => TypeMap;

    /// <summary>
    /// Gets the SQL Server column type for a given FHIR type name.
    /// </summary>
    /// <param name="fhirType">The FHIR type name (e.g., "string", "dateTime", "decimal").</param>
    /// <returns>The corresponding SQL Server column type definition.</returns>
    public static string GetSqlType(string? fhirType)
    {
        if (string.IsNullOrWhiteSpace(fhirType))
        {
            return DefaultSqlType;
        }

        return TypeMap.TryGetValue(fhirType, out string? sqlType) ? sqlType : DefaultSqlType;
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.RegularExpressions;

namespace Microsoft.Health.Fhir.SqlOnFhir.Materialization;

/// <summary>
/// Infers FHIR primitive types for ViewDefinition columns by combining:
/// (a) explicit <c>type</c> fields declared on columns in the ViewDefinition JSON,
/// (b) FHIRPath <c>ofType(X)</c> casts in the column path,
/// (c) SQL-on-FHIR helper functions (<c>getResourceKey</c>, <c>getReferenceKey</c>),
/// (d) well-known FHIR complex-type member lookups (e.g. <c>Quantity.value → decimal</c>),
/// (e) a fallback to the type returned by the schema evaluator.
/// <para>
/// This is important because the Ignixa schema evaluator commonly returns <c>null</c> or
/// <c>"string"</c> for paths that FHIR spec + FHIRPath can resolve precisely (e.g.
/// <c>value.ofType(Quantity).value</c> is <c>decimal</c> per FHIR spec).
/// </para>
/// </summary>
public static partial class ViewDefinitionTypeInferrer
{
    /// <summary>
    /// Well-known FHIR complex-type member → primitive type mapping.
    /// Keys are case-insensitive <c>ComplexType.memberName</c>.
    /// </summary>
    private static readonly Dictionary<string, string> ComplexTypeMembers = new(StringComparer.OrdinalIgnoreCase)
    {
        // Quantity (and its specializations: SimpleQuantity, Count, Duration, Distance, Age, Money)
        ["Quantity.value"] = "decimal",
        ["Quantity.comparator"] = "code",
        ["Quantity.unit"] = "string",
        ["Quantity.system"] = "uri",
        ["Quantity.code"] = "code",
        ["SimpleQuantity.value"] = "decimal",
        ["SimpleQuantity.unit"] = "string",
        ["SimpleQuantity.system"] = "uri",
        ["SimpleQuantity.code"] = "code",
        ["Age.value"] = "decimal",
        ["Duration.value"] = "decimal",
        ["Distance.value"] = "decimal",
        ["Count.value"] = "decimal",
        ["Money.value"] = "decimal",
        ["Money.currency"] = "code",

        // Reference
        ["Reference.reference"] = "string",
        ["Reference.type"] = "uri",
        ["Reference.display"] = "string",

        // Period
        ["Period.start"] = "dateTime",
        ["Period.end"] = "dateTime",

        // Coding
        ["Coding.system"] = "uri",
        ["Coding.version"] = "string",
        ["Coding.code"] = "code",
        ["Coding.display"] = "string",
        ["Coding.userSelected"] = "boolean",

        // CodeableConcept
        ["CodeableConcept.text"] = "string",

        // Identifier
        ["Identifier.use"] = "code",
        ["Identifier.system"] = "uri",
        ["Identifier.value"] = "string",

        // HumanName
        ["HumanName.use"] = "code",
        ["HumanName.text"] = "string",
        ["HumanName.family"] = "string",
        ["HumanName.given"] = "string",
        ["HumanName.prefix"] = "string",
        ["HumanName.suffix"] = "string",

        // ContactPoint
        ["ContactPoint.system"] = "code",
        ["ContactPoint.value"] = "string",
        ["ContactPoint.use"] = "code",
        ["ContactPoint.rank"] = "positiveInt",

        // Address
        ["Address.use"] = "code",
        ["Address.type"] = "code",
        ["Address.text"] = "string",
        ["Address.line"] = "string",
        ["Address.city"] = "string",
        ["Address.district"] = "string",
        ["Address.state"] = "string",
        ["Address.postalCode"] = "string",
        ["Address.country"] = "string",

        // Range
        ["Range.low"] = "decimal",
        ["Range.high"] = "decimal",

        // Ratio
        ["Ratio.numerator"] = "decimal",
        ["Ratio.denominator"] = "decimal",

        // Attachment
        ["Attachment.contentType"] = "code",
        ["Attachment.language"] = "code",
        ["Attachment.url"] = "url",
        ["Attachment.size"] = "integer64",
        ["Attachment.hash"] = "base64Binary",
        ["Attachment.title"] = "string",
        ["Attachment.creation"] = "dateTime",

        // Meta
        ["Meta.versionId"] = "id",
        ["Meta.lastUpdated"] = "instant",
        ["Meta.source"] = "uri",
        ["Meta.profile"] = "canonical",
    };

    [GeneratedRegex(@"\.ofType\(\s*([A-Za-z0-9_]+)\s*\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex OfTypeAtEndRegex();

    [GeneratedRegex(@"\.ofType\(\s*([A-Za-z0-9_]+)\s*\)\.([A-Za-z0-9_]+)(?:\s*$|[.(\[])", RegexOptions.IgnoreCase)]
    private static partial Regex OfTypeWithMemberRegex();

    /// <summary>
    /// Parses a ViewDefinition JSON and returns a map of column name → (path, explicit type).
    /// Walks the <c>select[*].column[*]</c> tree recursively (handles nested selects and unionAll).
    /// </summary>
    /// <param name="viewDefinitionJson">The ViewDefinition JSON string.</param>
    /// <returns>A dictionary keyed by column name; value tuple contains the declared path and optional explicit type.</returns>
    public static IReadOnlyDictionary<string, (string? Path, string? ExplicitType)> ExtractColumnMetadata(string viewDefinitionJson)
    {
        var result = new Dictionary<string, (string? Path, string? ExplicitType)>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(viewDefinitionJson))
        {
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(viewDefinitionJson);
            WalkSelect(doc.RootElement, result);
        }
        catch (JsonException)
        {
            // Malformed JSON — caller will fall back to evaluator types.
        }

        return result;
    }

    /// <summary>
    /// Resolves the best FHIR type for a column given its name, path, explicit type (from JSON),
    /// and the evaluator-provided type. Applies the inference rules in priority order:
    /// explicit-type → ofType() → helper-function → complex-type member → evaluator fallback.
    /// </summary>
    /// <param name="path">The FHIRPath expression for the column (may be null).</param>
    /// <param name="explicitType">The <c>type</c> field declared in the ViewDefinition JSON (may be null).</param>
    /// <param name="evaluatorType">The type returned by the Ignixa schema evaluator (may be null).</param>
    /// <returns>The resolved FHIR type string, or <c>null</c> if it can't be determined.</returns>
    public static string? ResolveType(string? path, string? explicitType, string? evaluatorType)
    {
        // Priority 1: explicit type declared on the column wins.
        if (!string.IsNullOrWhiteSpace(explicitType))
        {
            return explicitType;
        }

        // Priority 2: SQL-on-FHIR helper functions — always produce id.
        if (!string.IsNullOrWhiteSpace(path))
        {
            if (path.Contains("getResourceKey(", StringComparison.OrdinalIgnoreCase)
                || path.Contains("getReferenceKey(", StringComparison.OrdinalIgnoreCase))
            {
                return "id";
            }

            // Priority 3: path ends in .ofType(X) — X is the authoritative type.
            Match tailMatch = OfTypeAtEndRegex().Match(path);
            if (tailMatch.Success)
            {
                return tailMatch.Groups[1].Value;
            }

            // Priority 4: path contains .ofType(ComplexType).member — look up in map.
            Match memberMatch = OfTypeWithMemberRegex().Match(path);
            if (memberMatch.Success)
            {
                string complexType = memberMatch.Groups[1].Value;
                string member = memberMatch.Groups[2].Value;
                string key = $"{complexType}.{member}";
                if (ComplexTypeMembers.TryGetValue(key, out string? complexMemberType))
                {
                    return complexMemberType;
                }
            }
        }

        // Priority 5: whatever the evaluator returned (may be null or "string").
        return evaluatorType;
    }

    private static void WalkSelect(JsonElement node, IDictionary<string, (string? Path, string? ExplicitType)> result)
    {
        // A ViewDefinition or nested select node has: { "column": [...], "select": [...], "unionAll": [...] }
        if (node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (node.TryGetProperty("column", out JsonElement columns) && columns.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement col in columns.EnumerateArray())
            {
                if (col.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string? name = col.TryGetProperty("name", out JsonElement nameEl) ? nameEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                string? path = col.TryGetProperty("path", out JsonElement pathEl) ? pathEl.GetString() : null;
                string? explicitType = col.TryGetProperty("type", out JsonElement typeEl) ? typeEl.GetString() : null;

                result[name] = (path, explicitType);
            }
        }

        if (node.TryGetProperty("select", out JsonElement selects) && selects.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement sel in selects.EnumerateArray())
            {
                WalkSelect(sel, result);
            }
        }

        if (node.TryGetProperty("unionAll", out JsonElement unions) && unions.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement u in unions.EnumerateArray())
            {
                WalkSelect(u, result);
            }
        }
    }
}

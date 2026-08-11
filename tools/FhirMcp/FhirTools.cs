// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;

namespace Microsoft.Health.Fhir.Mcp;

[McpServerToolType]
internal sealed class FhirTools
{
    private const string VectorSearchConfigUrl = "http://microsoft.com/fhir/StructureDefinition/vector-search-config";
    private static readonly Regex FhirIdRegex = new("^[A-Za-z0-9.-]{1,64}$", RegexOptions.CultureInvariant);
    private static readonly Regex ResourceTypeRegex = new("^[A-Z][A-Za-z0-9]{0,63}$", RegexOptions.CultureInvariant);
    private static readonly Regex SearchParameterRegex = new(
        "^_?[A-Za-z][A-Za-z0-9-]*(?::[A-Za-z][A-Za-z0-9-]*)?(?:\\.[A-Za-z][A-Za-z0-9-]*(?::[A-Za-z][A-Za-z0-9-]*)?)*$",
        RegexOptions.CultureInvariant);

    private static readonly string[] SearchParameterPath = ["SearchParameter"];
    private static readonly string[] SearchParameterStatusPath = ["SearchParameter", "$status"];

    private readonly IFhirClient _fhirClient;
    private readonly FhirMcpOptions _options;

    public FhirTools(IFhirClient fhirClient, FhirMcpOptions options)
    {
        _fhirClient = fhirClient;
        _options = options;
    }

    [McpServerTool(Name = "patientSemanticSearch", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Semantically rank FHIR resources in one patient compartment. Use structured FHIR search for exact values, dates, codes, and statuses.")]
    public async Task<FhirToolResult> PatientSemanticSearchAsync(
        [Description("FHIR Patient resource id, without the 'Patient/' prefix.")] string patientId,
        [Description("Natural-language clinical retrieval question.")] string query,
        [Description("Optional resource types to rank. Omit to use every type supported by the server operation.")] string[]? resourceTypes = null,
        [Description("Maximum number of ranked resources to return.")] int count = 10,
        CancellationToken cancellationToken = default)
    {
        ValidateFhirId(patientId, nameof(patientId));
        ValidateText(query, nameof(query));
        ValidateCount(count);

        string[] distinctResourceTypes = (resourceTypes ?? Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (string resourceType in distinctResourceTypes)
        {
            ValidateResourceType(resourceType);
            await _fhirClient.EnsureResourceTypeAsync(resourceType, cancellationToken);
        }

        var parameters = new List<Dictionary<string, object>>
        {
            new() { ["name"] = "query", ["valueString"] = query },
            new() { ["name"] = "count", ["valueInteger"] = count },
        };
        parameters.AddRange(distinctResourceTypes.Select(resourceType =>
            new Dictionary<string, object> { ["name"] = "type", ["valueCode"] = resourceType }));

        string requestBody = JsonSerializer.Serialize(new { resourceType = "Parameters", parameter = parameters });
        FhirResponse response = await _fhirClient.PostAsync(
            "patient-semantic-search",
            new[] { "Patient", patientId, "$semantic-search" },
            requestBody,
            cancellationToken);
        EnsureResponseType(response, "Bundle");
        return FhirToolResult.FromResponse(response);
    }

    [McpServerTool(Name = "searchFhirResources", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Run a standard FHIR resource search. Generate filters from the clinical question; each filter value is encoded and repeated values are preserved.")]
    public async Task<FhirToolResult> SearchFhirResourcesAsync(
        [Description("FHIR resource type to search, such as Observation or Encounter.")] string resourceType,
        [Description("FHIR search parameters mapped to one or more values, for example { patient: [\"123\"], date: [\"ge2025-01-01\"] }.")] Dictionary<string, string[]>? filters = null,
        [Description("Maximum number of resources to return.")] int count = 20,
        CancellationToken cancellationToken = default)
    {
        ValidateResourceType(resourceType);
        ValidateCount(count);
        await _fhirClient.EnsureResourceTypeAsync(resourceType, cancellationToken);

        var queryParameters = new List<KeyValuePair<string, string>>();
        foreach ((string name, string[] values) in filters ?? new Dictionary<string, string[]>())
        {
            ValidateSearchParameter(name, values);
            queryParameters.AddRange(values.Select(value => new KeyValuePair<string, string>(name, value)));
        }

        queryParameters.Add(new KeyValuePair<string, string>("_count", count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        FhirResponse response = await _fhirClient.GetAsync(
            "search-fhir-resources",
            new[] { resourceType },
            queryParameters,
            cancellationToken);
        EnsureResponseType(response, "Bundle");
        return FhirToolResult.FromResponse(response);
    }

    [McpServerTool(Name = "readFhirResource", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Read one FHIR resource by type and id, optionally at an exact history version.")]
    public async Task<FhirToolResult> ReadFhirResourceAsync(
        [Description("FHIR resource type, such as Observation or Binary.")] string resourceType,
        [Description("FHIR logical resource id.")] string id,
        [Description("Optional FHIR version id for a vread.")] string? versionId = null,
        CancellationToken cancellationToken = default)
    {
        ValidateResourceType(resourceType);
        ValidateFhirId(id, nameof(id));
        if (versionId is not null)
        {
            ValidateFhirId(versionId, nameof(versionId));
        }

        await _fhirClient.EnsureResourceTypeAsync(resourceType, cancellationToken);
        string[] pathSegments = versionId is null
            ? new[] { resourceType, id }
            : new[] { resourceType, id, "_history", versionId };
        FhirResponse response = await _fhirClient.GetAsync(
            "read-fhir-resource",
            pathSegments,
            Array.Empty<KeyValuePair<string, string>>(),
            cancellationToken);
        EnsureResponseType(response, resourceType);
        return FhirToolResult.FromResponse(response);
    }

    [McpServerTool(Name = "discoverVectorSearchParameters", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Discover posted FHIR SearchParameters configured for vector search, including base types, expressions, vector configuration, and live activation status.")]
    public async Task<VectorSearchParameterDiscoveryResult> DiscoverVectorSearchParametersAsync(CancellationToken cancellationToken = default)
    {
        FhirResponse searchResponse = await _fhirClient.GetAsync(
            "discover-vector-search-parameters",
            SearchParameterPath,
            new[]
            {
                new KeyValuePair<string, string>("type", "special"),
                new KeyValuePair<string, string>("_count", _options.MaxCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            },
            cancellationToken);
        EnsureResponseType(searchResponse, "Bundle");

        var captures = new List<string> { searchResponse.CaptureDirectory };
        var definitions = new List<VectorSearchParameterDefinition>();
        foreach (JsonElement resource in EnumerateBundleResources(searchResponse.Resource))
        {
            JsonElement? vectorConfiguration = FindVectorConfiguration(resource);
            string? canonicalUrl = GetOptionalString(resource, "url");
            if (vectorConfiguration is null || canonicalUrl is null)
            {
                continue;
            }

            FhirResponse statusResponse = await _fhirClient.GetAsync(
                "read-vector-search-parameter-status",
                SearchParameterStatusPath,
                new[] { new KeyValuePair<string, string>("url", canonicalUrl) },
                cancellationToken);
            EnsureResponseType(statusResponse, "Parameters");
            captures.Add(statusResponse.CaptureDirectory);

            definitions.Add(new VectorSearchParameterDefinition(
                GetOptionalString(resource, "id"),
                canonicalUrl,
                GetOptionalString(resource, "version"),
                GetOptionalString(resource, "name"),
                GetOptionalString(resource, "code"),
                GetStringArray(resource, "base"),
                GetOptionalString(resource, "expression"),
                GetOptionalString(resource, "status"),
                GetActivationStatus(statusResponse.Resource),
                vectorConfiguration.Value));
        }

        return new VectorSearchParameterDiscoveryResult(definitions, captures);
    }

    private static IEnumerable<JsonElement> EnumerateBundleResources(JsonElement bundle)
    {
        if (!bundle.TryGetProperty("entry", out JsonElement entries))
        {
            yield break;
        }

        foreach (JsonElement entry in entries.EnumerateArray())
        {
            if (entry.TryGetProperty("resource", out JsonElement resource))
            {
                yield return resource;
            }
        }
    }

    private static JsonElement? FindVectorConfiguration(JsonElement searchParameter)
    {
        if (!searchParameter.TryGetProperty("extension", out JsonElement extensions))
        {
            return null;
        }

        foreach (JsonElement extension in extensions.EnumerateArray())
        {
            if (GetOptionalString(extension, "url") == VectorSearchConfigUrl)
            {
                return extension.Clone();
            }
        }

        return null;
    }

    private static string? GetActivationStatus(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("parameter", out JsonElement parameterElements))
        {
            return null;
        }

        foreach (JsonElement parameter in parameterElements.EnumerateArray())
        {
            if (GetOptionalString(parameter, "name") != "searchParameterStatus" ||
                !parameter.TryGetProperty("part", out JsonElement parts))
            {
                continue;
            }

            foreach (JsonElement part in parts.EnumerateArray())
            {
                if (GetOptionalString(part, "name") == "status")
                {
                    return GetOptionalString(part, "valueString");
                }
            }
        }

        return null;
    }

    private static string? GetOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string[] GetStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement values))
        {
            return Array.Empty<string>();
        }

        return values.EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString()!)
            .ToArray();
    }

    private static void EnsureResponseType(FhirResponse response, string expectedResourceType)
    {
        string? actualResourceType = GetOptionalString(response.Resource, "resourceType");
        if (!string.Equals(actualResourceType, expectedResourceType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"FHIR response from {response.RequestUri} was '{actualResourceType ?? "unknown"}', expected '{expectedResourceType}'. Capture: {response.CaptureDirectory}");
        }
    }

    private static void ValidateFhirId(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !FhirIdRegex.IsMatch(value))
        {
            throw new ArgumentException("FHIR ids must contain 1-64 letters, digits, hyphens, or periods.", parameterName);
        }
    }

    private static void ValidateResourceType(string resourceType)
    {
        if (string.IsNullOrWhiteSpace(resourceType) || !ResourceTypeRegex.IsMatch(resourceType))
        {
            throw new ArgumentException("FHIR resource types must start with an uppercase letter and contain only letters or digits.", nameof(resourceType));
        }
    }

    private static void ValidateText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value must not be empty.", parameterName);
        }
    }

    private void ValidateCount(int count)
    {
        if (count < 1 || count > _options.MaxCount)
        {
            throw new ArgumentOutOfRangeException(nameof(count), $"Count must be between 1 and {_options.MaxCount}.");
        }
    }

    private static void ValidateSearchParameter(string name, string[] values)
    {
        if (name == "_count" || name == "_format" || !SearchParameterRegex.IsMatch(name))
        {
            throw new ArgumentException($"FHIR search parameter '{name}' is not allowed.", nameof(name));
        }

        if (values is null || values.Length == 0 || values.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException($"FHIR search parameter '{name}' requires at least one non-empty value.", nameof(values));
        }
    }
}

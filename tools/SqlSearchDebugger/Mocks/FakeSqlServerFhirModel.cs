// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace SqlSearchDebugger.Mocks;

class FakeSqlServerFhirModel : ISqlServerFhirModel
{
    private readonly IReadOnlyDictionary<string, short> _resourceTypeNameToId;
    private readonly IReadOnlyDictionary<short, string> _resourceTypeIdToName;
    private readonly ConcurrentDictionary<string, short> _searchParamUriToId;
    private readonly IReadOnlyList<object> _resourceTypes;

    public FakeSqlServerFhirModel(
        IEnumerable<string> resourceTypeNames,
        IEnumerable<Uri> searchParameterUris)
    {
        KeyValuePair<string, short>[] resourceTypes = resourceTypeNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select((name, index) => KeyValuePair.Create(name, checked((short)(index + 1))))
            .ToArray();

        _resourceTypeNameToId = resourceTypes.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        _resourceTypeIdToName = resourceTypes.ToDictionary(pair => pair.Value, pair => pair.Key);
        _resourceTypes = resourceTypes
            .Select(pair => (object)new { name = pair.Key, id = pair.Value })
            .ToArray();
        _searchParamUriToId = new ConcurrentDictionary<string, short>(
            searchParameterUris
                .Where(uri => uri != null)
                .Select(uri => uri.OriginalString)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(uri => uri, StringComparer.Ordinal)
                .Select((uri, index) => KeyValuePair.Create(uri, checked((short)(index + 1)))),
            StringComparer.Ordinal);
    }

    public int ResourceTypeCount => _resourceTypeNameToId.Count;

    public int SearchParamCount => _searchParamUriToId.Count;

    public (short lowestId, short highestId) ResourceTypeIdRange =>
        _resourceTypeNameToId.Count > 0
            ? ((short)1, checked((short)_resourceTypeNameToId.Count))
            : ((short)0, (short)0);

    public short GetResourceTypeId(string resourceTypeName) =>
        _resourceTypeNameToId.TryGetValue(resourceTypeName, out short id)
            ? id
            : throw new KeyNotFoundException($"Unknown FHIR resource type '{resourceTypeName}'.");

    public bool TryGetResourceTypeId(string resourceTypeName, out short id) =>
        _resourceTypeNameToId.TryGetValue(resourceTypeName, out id);

    public string GetResourceTypeName(short resourceTypeId) =>
        _resourceTypeIdToName.TryGetValue(resourceTypeId, out string? name)
            ? name
            : throw new KeyNotFoundException($"Unknown FHIR resource type ID '{resourceTypeId}'.");

    public byte GetClaimTypeId(string claimTypeName) => 1;

    public short GetSearchParamId(Uri searchParamUri)
    {
        ArgumentNullException.ThrowIfNull(searchParamUri);

        return _searchParamUriToId.TryGetValue(searchParamUri.OriginalString, out short id)
            ? id
            : throw new KeyNotFoundException($"Unknown search parameter '{searchParamUri}'.");
    }

    public void TryAddSearchParamIdToUriMapping(string searchParamUri, short searchParamId) =>
        _searchParamUriToId.TryAdd(searchParamUri, searchParamId);

    public void RemoveSearchParamIdToUriMapping(string searchParamUri) =>
        _searchParamUriToId.TryRemove(searchParamUri, out _);

    public byte GetCompartmentTypeId(string compartmentType) => 1;

    public bool TryGetSystemId(string system, out int systemId)
    {
        systemId = GetStableId(system);
        return true;
    }

    public int GetSystemId(string system) => GetStableId(system);

    public int GetQuantityCodeId(string code) => GetStableId(code);

    public bool TryGetQuantityCodeId(string code, out int quantityCodeId)
    {
        quantityCodeId = GetStableId(code);
        return true;
    }

    public List<object> GetAllResourceTypes() => [.. _resourceTypes];

    private static int GetStableId(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char character in value)
            {
                hash = (hash ^ char.ToUpperInvariant(character)) * 16777619;
            }

            return (int)((hash & 0x7FFFFFFF) | 1);
        }
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace SqlSearchDebugger.Mocks;

class FakeSqlServerFhirModel : ISqlServerFhirModel
{
    private readonly Dictionary<string, short> _resourceTypeNameToId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<short, string> _resourceTypeIdToName = new();
    private readonly Dictionary<string, short> _searchParamUriToId = new();
    private readonly Dictionary<string, int> _systemToId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _quantityCodeToId = new(StringComparer.OrdinalIgnoreCase);
    private short _nextResourceTypeId = 1;
    private short _nextSearchParamId = 1;
    private int _nextSystemId = 1;
    private int _nextQuantityCodeId = 1;

    public int ResourceTypeCount => _resourceTypeNameToId.Count;
    public int SearchParamCount => _searchParamUriToId.Count;

    public (short lowestId, short highestId) ResourceTypeIdRange =>
        _resourceTypeNameToId.Count > 0
            ? ((short)1, (short)(_nextResourceTypeId - 1))
            : ((short)0, (short)0);

    public short GetResourceTypeId(string resourceTypeName)
    {
        if (_resourceTypeNameToId.TryGetValue(resourceTypeName, out var id))
        {
            return id;
        }

        id = _nextResourceTypeId++;
        _resourceTypeNameToId[resourceTypeName] = id;
        _resourceTypeIdToName[id] = resourceTypeName;
        return id;
    }

    public bool TryGetResourceTypeId(string resourceTypeName, out short id)
    {
        if (_resourceTypeNameToId.TryGetValue(resourceTypeName, out id))
        {
            return true;
        }

        id = GetResourceTypeId(resourceTypeName);
        return true;
    }

    public string GetResourceTypeName(short resourceTypeId)
    {
        if (_resourceTypeIdToName.TryGetValue(resourceTypeId, out var name))
        {
            return name;
        }

        return $"UnknownType_{resourceTypeId}";
    }

    public byte GetClaimTypeId(string claimTypeName) => 1;

    public short GetSearchParamId(Uri searchParamUri)
    {
        if (searchParamUri == null)
        {
            return 0;
        }

        var key = searchParamUri.OriginalString;
        if (_searchParamUriToId.TryGetValue(key, out var id))
        {
            return id;
        }

        id = _nextSearchParamId++;
        _searchParamUriToId[key] = id;
        return id;
    }

    public void TryAddSearchParamIdToUriMapping(string searchParamUri, short searchParamId)
    {
        _searchParamUriToId[searchParamUri] = searchParamId;
    }

    public void RemoveSearchParamIdToUriMapping(string searchParamUri)
    {
        _searchParamUriToId.Remove(searchParamUri);
    }

    public byte GetCompartmentTypeId(string compartmentType) => 1;

    public bool TryGetSystemId(string system, out int systemId)
    {
        if (_systemToId.TryGetValue(system, out systemId))
        {
            return true;
        }

        systemId = _nextSystemId++;
        _systemToId[system] = systemId;
        return true;
    }

    public int GetSystemId(string system)
    {
        TryGetSystemId(system, out var id);
        return id;
    }

    public int GetQuantityCodeId(string code)
    {
        TryGetQuantityCodeId(code, out var id);
        return id;
    }

    public bool TryGetQuantityCodeId(string code, out int quantityCodeId)
    {
        if (_quantityCodeToId.TryGetValue(code, out quantityCodeId))
        {
            return true;
        }

        quantityCodeId = _nextQuantityCodeId++;
        _quantityCodeToId[code] = quantityCodeId;
        return true;
    }

    public List<object> GetAllResourceTypes()
    {
        return _resourceTypeNameToId.Select(kvp => (object)new { name = kvp.Key, id = kvp.Value })
            .OrderBy(x => ((dynamic)x).id)
            .ToList();
    }
}

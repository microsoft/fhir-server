// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "This is a collection of search parameters")]
    public class SearchParameterCollection
    {
        private readonly Dictionary<int, Dictionary<string, SearchParameter>> _parametersByCode;
        private readonly Dictionary<int, SearchParameter> _parametersById;
        private readonly ISqlServerFhirModel _sqlServerFhirModel;
        private readonly IModelInfoProvider _modelInfoProvider;

        public SearchParameterCollection(ISqlServerFhirModel sqlServerFhirModel, IModelInfoProvider modelInfoProvider)
        {
            ArgumentNullException.ThrowIfNull(sqlServerFhirModel);
            ArgumentNullException.ThrowIfNull(modelInfoProvider);

            _sqlServerFhirModel = sqlServerFhirModel;
            _modelInfoProvider = modelInfoProvider;
            _parametersByCode = new Dictionary<int, Dictionary<string, SearchParameter>>();
            _parametersById = new Dictionary<int, SearchParameter>();
        }

        public SearchParameterCollection(IEnumerable<SearchParameter> parameters, ISqlServerFhirModel sqlServerFhirModel, IModelInfoProvider modelInfoProvider)
            : this(sqlServerFhirModel, modelInfoProvider)
        {
            foreach (var parameter in parameters)
            {
                Add(parameter);
            }
        }

        public int Count => _parametersById.Count;

        public IEnumerable<SearchParameter> All => _parametersById.Values;

        public void Add(SearchParameter parameter)
        {
            ArgumentNullException.ThrowIfNull(parameter);

            foreach (var resourceType in GetDerivedResourceTypes(parameter.ResourceTypes))
            {
                var resourceTypeId = _sqlServerFhirModel.GetResourceTypeId(resourceType);
                if (!_parametersByCode.TryGetValue(resourceTypeId, out var parametersForResourceType))
                {
                    parametersForResourceType = new Dictionary<string, SearchParameter>(StringComparer.Ordinal);
                    _parametersByCode[resourceTypeId] = parametersForResourceType;
                }

                parametersForResourceType[parameter.Code] = parameter;
            }
        }

        public SearchParameter? GetByCode(string code, int resourceType)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return null;
            }

            if (code.Contains(':', StringComparison.OrdinalIgnoreCase))
            {
                code = code.Split(':', 2, StringSplitOptions.None)[0];
            }

            if (_parametersByCode.TryGetValue(resourceType, out var parameters) && parameters.TryGetValue(code, out var parameter))
            {
                return parameter;
            }

            return null;
        }

        public SearchParameter? GetById(int id)
        {
            _parametersById.TryGetValue(id, out var parameter);
            return parameter;
        }

        public string? GetParameterType(string code, int resourceType)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return null;
            }

            var parameter = GetByCode(code, resourceType);
            return parameter?.Type;
        }

        private HashSet<string> GetDerivedResourceTypes(IReadOnlyCollection<string> resourceTypes)
        {
            var completeResourceList = new HashSet<string>(resourceTypes);

            foreach (var baseResourceType in resourceTypes)
            {
                if (baseResourceType == KnownResourceTypes.Resource)
                {
                    completeResourceList.UnionWith(_modelInfoProvider.GetResourceTypeNames().ToHashSet());

                    // We added all possible resource types, so no need to continue
                    break;
                }

                if (baseResourceType == KnownResourceTypes.DomainResource)
                {
                    var domainResourceChildResourceTypes = _modelInfoProvider.GetResourceTypeNames().ToHashSet();

                    // Remove types that inherit from Resource directly
                    domainResourceChildResourceTypes.Remove(KnownResourceTypes.Binary);
                    domainResourceChildResourceTypes.Remove(KnownResourceTypes.Bundle);
                    domainResourceChildResourceTypes.Remove(KnownResourceTypes.Parameters);

                    completeResourceList.UnionWith(domainResourceChildResourceTypes);
                }
            }

            if (completeResourceList.Count == 0)
            {
                throw new ArgumentException("No resource types were found for the given search parameter.", nameof(resourceTypes));
            }

            return completeResourceList;
        }
    }
}

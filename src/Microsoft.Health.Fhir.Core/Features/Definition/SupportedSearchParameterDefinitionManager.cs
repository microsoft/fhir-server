// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    /// <summary>
    /// A SearchParameterDefinitionManager that only returns actively searchable parameters.
    /// </summary>
    public class SupportedSearchParameterDefinitionManager : ISupportedSearchParameterDefinitionManager
    {
        private readonly ISearchParameterDefinitionManager _inner;

        public SupportedSearchParameterDefinitionManager(ISearchParameterDefinitionManager inner)
        {
            EnsureArg.IsNotNull(inner, nameof(inner));

            _inner = inner;
        }

        public IEnumerable<SearchParameterInfo> AllSearchParameters => _inner.AllSearchParameters.Where(x => x.IsSupported);

        public IReadOnlyDictionary<string, string> SearchParameterHashMap => _inner.SearchParameterHashMap;

        public IEnumerable<SearchParameterInfo> GetSearchParameters(string resourceType)
        {
            return _inner.GetSearchParameters(resourceType)
                .Where(x => x.IsSupported);
        }

        public bool TryGetSearchParameter(string resourceType, string code, out SearchParameterInfo searchParameter)
        {
            searchParameter = null;
            if (_inner.TryGetSearchParameter(resourceType, code, out var parameter) && parameter.IsSupported)
            {
                searchParameter = parameter;

                return true;
            }

            return false;
        }

        public SearchParameterInfo GetSearchParameter(string resourceType, string code)
        {
            SearchParameterInfo parameter = _inner.GetSearchParameter(resourceType, code);
            if (parameter.IsSupported)
            {
                return parameter;
            }

            throw new SearchParameterNotSupportedException(resourceType, code);
        }

        public SearchParameterInfo GetSearchParameter(string definitionUri)
        {
            SearchParameterInfo parameter = _inner.GetSearchParameter(definitionUri);
            if (parameter.IsSupported)
            {
                return parameter;
            }

            throw new SearchParameterNotSupportedException(definitionUri);
        }

        public string GetSearchParameterHashForResourceType(string resourceType)
        {
            return _inner.GetSearchParameterHashForResourceType(resourceType);
        }

        public void AddNewSearchParameters(IReadOnlyCollection<ITypedElement> searchParameters, bool calculateHash = true)
        {
            _inner.AddNewSearchParameters(searchParameters, calculateHash);
        }

        public void UpdateSearchParameterHashMap(Dictionary<string, string> updatedSearchParamHashMap)
        {
            _inner.UpdateSearchParameterHashMap(updatedSearchParamHashMap);
        }

        public void DeleteSearchParameter(ITypedElement searchParam)
        {
            _inner.DeleteSearchParameter(searchParam);
        }

        public bool TryGetSearchParameter(string definitionUri, out SearchParameterInfo value)
        {
            value = null;
            if (_inner.TryGetSearchParameter(definitionUri, out var parameter) && parameter.IsSupported)
            {
                value = parameter;

                return true;
            }

            return false;
        }

        public void DeleteSearchParameter(string url, bool calculateHash = true)
        {
            throw new NotImplementedException();
        }
    }
}

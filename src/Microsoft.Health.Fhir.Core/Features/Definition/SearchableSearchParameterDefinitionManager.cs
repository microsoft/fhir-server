// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    /// <summary>
    /// A SearchParameterDefinitionManager that only returns actively searchable parameters.
    /// </summary>
    public class SearchableSearchParameterDefinitionManager : ISearchParameterDefinitionManager
    {
        private readonly ISearchParameterDefinitionManager _inner;
        private RequestContextAccessor<IFhirRequestContext> _fhirReqeustContextAccessor;

        public SearchableSearchParameterDefinitionManager(ISearchParameterDefinitionManager inner, RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor)
        {
            EnsureArg.IsNotNull(inner, nameof(inner));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));

            _inner = inner;
            _fhirReqeustContextAccessor = fhirRequestContextAccessor;
        }

        public IEnumerable<SearchParameterInfo> AllSearchParameters => GetAllSearchParameters();

        public IReadOnlyDictionary<string, string> SearchParameterHashMap => _inner.SearchParameterHashMap;

        public IEnumerable<SearchParameterInfo> GetSearchParameters(string resourceType)
        {
            if (_fhirReqeustContextAccessor.RequestContext != null
                && _fhirReqeustContextAccessor.RequestContext.IncludePartiallyIndexedSearchParams)
            {
                return _inner.GetSearchParameters(resourceType)
                .Where(x => x.IsSupported);
            }
            else
            {
                return _inner.GetSearchParameters(resourceType)
                    .Where(x => x.IsSearchable);
            }
        }

        public bool TryGetSearchParameter(string resourceType, string code, out SearchParameterInfo searchParameter)
        {
            searchParameter = null;

            if (_inner.TryGetSearchParameter(resourceType, code, out var parameter) &&
                (parameter.IsSearchable || UsePartialSearchParams(parameter)))
            {
                searchParameter = parameter;

                return true;
            }

            return false;
        }

        public SearchParameterInfo GetSearchParameter(string resourceType, string code)
        {
            SearchParameterInfo parameter = _inner.GetSearchParameter(resourceType, code);

            if (parameter.IsSearchable || UsePartialSearchParams(parameter))
            {
                return parameter;
            }

            throw new SearchParameterNotSupportedException(resourceType, code);
        }

        public SearchParameterInfo GetSearchParameter(string definitionUri)
        {
            SearchParameterInfo parameter = _inner.GetSearchParameter(definitionUri);

            if (parameter.IsSearchable || UsePartialSearchParams(parameter))
            {
                return parameter;
            }

            throw new SearchParameterNotSupportedException(definitionUri);
        }

        private IEnumerable<SearchParameterInfo> GetAllSearchParameters()
        {
            if (_fhirReqeustContextAccessor.RequestContext != null &&
                _fhirReqeustContextAccessor.RequestContext.IncludePartiallyIndexedSearchParams)
            {
                return _inner.AllSearchParameters.Where(x => x.IsSupported);
            }
            else
            {
                return _inner.AllSearchParameters.Where(x => x.IsSearchable);
            }
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
            _inner.TryGetSearchParameter(definitionUri, out var parameter);

            if (parameter.IsSearchable || UsePartialSearchParams(parameter))
            {
                value = parameter;
                return true;
            }

            value = null;
            return false;
        }

        public void DeleteSearchParameter(string url, bool calculateHash = true)
        {
            throw new NotImplementedException();
        }

        private bool UsePartialSearchParams(SearchParameterInfo parameter)
        {
            return _fhirReqeustContextAccessor.RequestContext != null &&
                   _fhirReqeustContextAccessor.RequestContext.IncludePartiallyIndexedSearchParams &&
                   parameter.IsSupported;
        }

        public IEnumerable<SearchParameterInfo> GetSearchParametersByResourceTypes(ICollection<string> resourceTypes)
        {
            return _inner.GetSearchParametersByResourceTypes(resourceTypes);
        }

        public IEnumerable<SearchParameterInfo> GetSearchParametersByUrls(ICollection<string> definitionUrls)
        {
            return _inner.GetSearchParametersByUrls(definitionUrls);
        }

        public IEnumerable<SearchParameterInfo> GetSearchParametersByCodes(ICollection<string> codes)
        {
            return _inner.GetSearchParametersByCodes(codes);
        }

        public IEnumerable<SearchParameterInfo> GetSearchParametersByIds(ICollection<string> ids)
        {
            return _inner.GetSearchParametersByIds(ids);
        }
    }
}

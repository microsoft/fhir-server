// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
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
        private SearchParameterStatusManager _statusManager;

        public SearchableSearchParameterDefinitionManager(ISearchParameterDefinitionManager inner, RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor, SearchParameterStatusManager statusManager)
        {
            EnsureArg.IsNotNull(inner, nameof(inner));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(statusManager, nameof(statusManager));

            _inner = inner;
            _fhirReqeustContextAccessor = fhirRequestContextAccessor;
            _statusManager = statusManager;
        }

        public IEnumerable<SearchParameterInfo> AllSearchParameters => GetAllSearchParameters();

        public IReadOnlyDictionary<string, string> SearchParameterHashMap => _inner.SearchParameterHashMap;

        public IEnumerable<SearchParameterInfo> GetSearchParameters(string resourceType)
        {
            if (_fhirReqeustContextAccessor.RequestContext != null
                && _fhirReqeustContextAccessor.RequestContext.IncludePartiallyIndexedSearchParams)
            {
                return _inner.GetSearchParameters(resourceType)
                .Where(x => x.IsSupported && IsEnabled(x));
            }
            else
            {
                return _inner.GetSearchParameters(resourceType)
                    .Where(x => x.IsSearchable && IsEnabled(x));
            }
        }

        public bool TryGetSearchParameter(string resourceType, string code, out SearchParameterInfo searchParameter)
        {
            searchParameter = null;

            if (_inner.TryGetSearchParameter(resourceType, code, out var parameter) &&
                (parameter.IsSearchable || UsePartialSearchParams(parameter)) && IsEnabled(parameter))
            {
                searchParameter = parameter;

                return true;
            }

            return false;
        }

        public SearchParameterInfo GetSearchParameter(string resourceType, string code)
        {
            SearchParameterInfo parameter = _inner.GetSearchParameter(resourceType, code);

            if ((parameter.IsSearchable || UsePartialSearchParams(parameter)) && IsEnabled(parameter))
            {
                return parameter;
            }

            throw new SearchParameterNotSupportedException(resourceType, code);
        }

        public SearchParameterInfo GetSearchParameter(string definitionUri)
        {
            SearchParameterInfo parameter = _inner.GetSearchParameter(definitionUri);

            if ((parameter.IsSearchable || UsePartialSearchParams(parameter)) && IsEnabled(parameter))
            {
                return parameter;
            }

            throw new SearchParameterNotSupportedException(definitionUri);
        }

        private IEnumerable<SearchParameterInfo> GetAllSearchParameters()
        {
            var searchParameterStatuses = _statusManager.GetAllSearchParameterStatus(default).ConfigureAwait(false).GetAwaiter().GetResult();

            if (_fhirReqeustContextAccessor.RequestContext != null &&
                _fhirReqeustContextAccessor.RequestContext.IncludePartiallyIndexedSearchParams)
            {
                return _inner.AllSearchParameters.Where(x => x.IsSupported && IsEnabled(x));
            }
            else
            {
                return _inner.AllSearchParameters.Where(x => x.IsSearchable && IsEnabled(x));
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

            if ((parameter.IsSearchable || UsePartialSearchParams(parameter)) && IsEnabled(parameter))
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

        private bool IsEnabled(SearchParameterInfo parameter)
        {
            if (parameter.Code == "_type")
            {
                return true;
            }

            var searchParameterStatuses = _statusManager.GetAllSearchParameterStatus(default).ConfigureAwait(false).GetAwaiter().GetResult();
            return searchParameterStatuses.Where(sp => sp.Uri.OriginalString.Equals(parameter.Url.OriginalString, StringComparison.OrdinalIgnoreCase)).First().Status == SearchParameterStatus.Enabled;
        }
    }
}

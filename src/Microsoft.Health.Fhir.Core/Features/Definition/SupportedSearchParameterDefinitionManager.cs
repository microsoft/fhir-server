// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    /// <summary>
    /// A SearchParameterDefinitionManager that only returns actively searchable parameters.
    /// </summary>
    public class SupportedSearchParameterDefinitionManager : ISearchParameterDefinitionManager
    {
        private readonly SearchParameterDefinitionManager _inner;

        public SupportedSearchParameterDefinitionManager(SearchParameterDefinitionManager inner)
        {
            EnsureArg.IsNotNull(inner, nameof(inner));

            _inner = inner;
        }

        public IEnumerable<SearchParameterInfo> AllSearchParameters => _inner.AllSearchParameters.Where(x => x.IsSupported);

        public IEnumerable<SearchParameterInfo> GetSearchParameters(string resourceType)
        {
            return _inner.GetSearchParameters(resourceType)
                .Where(x => x.IsSupported);
        }

        public bool TryGetSearchParameter(string resourceType, string name, out SearchParameterInfo searchParameter)
        {
            searchParameter = null;
            if (_inner.TryGetSearchParameter(resourceType, name, out var parameter) && parameter.IsSupported)
            {
                searchParameter = parameter;

                return true;
            }

            return false;
        }

        public SearchParameterInfo GetSearchParameter(string resourceType, string name)
        {
            SearchParameterInfo parameter = _inner.GetSearchParameter(resourceType, name);
            if (parameter.IsSupported)
            {
                return parameter;
            }

            throw new SearchParameterNotSupportedException(resourceType, name);
        }

        public SearchParameterInfo GetSearchParameter(Uri definitionUri)
        {
            SearchParameterInfo parameter = _inner.GetSearchParameter(definitionUri);
            if (parameter.IsSupported)
            {
                return parameter;
            }

            throw new SearchParameterNotSupportedException(definitionUri);
        }

        public SearchParamType GetSearchParameterType(SearchParameterInfo searchParameter, int? componentIndex)
        {
            return _inner.GetSearchParameterType(searchParameter, componentIndex);
        }

        /// <summary>
        /// This method will filter by isSupported = true always and return search parameters
        /// based on the value supplied for isSearchable
        /// </summary>
        /// <param name="isSupported">Overidden to be true.</param>
        /// <param name="isSearchable">Value to filter on whether or not search params are searchable.</param>
        /// <returns>Returns all search parameters that are both supported and searchable</returns>
        public IEnumerable<SearchParameterInfo> GetSearchByStatus(bool isSupported, bool isSearchable)
        {
            return _inner.GetSearchByStatus(true, isSearchable);
        }
    }
}

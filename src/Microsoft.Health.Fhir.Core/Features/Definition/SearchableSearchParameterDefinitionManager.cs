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
    public class SearchableSearchParameterDefinitionManager : ISearchParameterDefinitionManager
    {
        private readonly SearchParameterDefinitionManager _inner;

        public SearchableSearchParameterDefinitionManager(SearchParameterDefinitionManager inner)
        {
            EnsureArg.IsNotNull(inner, nameof(inner));

            _inner = inner;
        }

        public IEnumerable<SearchParameterInfo> AllSearchParameters => _inner.AllSearchParameters.Where(x => x.IsSearchable);

        public IEnumerable<SearchParameterInfo> GetSearchParameters(string resourceType)
        {
            return _inner.GetSearchParameters(resourceType)
                .Where(x => x.IsSearchable);
        }

        public bool TryGetSearchParameter(string resourceType, string name, out SearchParameterInfo searchParameter)
        {
            searchParameter = null;

            if (_inner.TryGetSearchParameter(resourceType, name, out var parameter) && parameter.IsSearchable)
            {
                searchParameter = parameter;

                return true;
            }

            return false;
        }

        public SearchParameterInfo GetSearchParameter(string resourceType, string name)
        {
            SearchParameterInfo parameter = _inner.GetSearchParameter(resourceType, name);

            if (parameter.IsSearchable)
            {
                return parameter;
            }

            throw new SearchParameterNotSupportedException(resourceType, name);
        }

        public SearchParameterInfo GetSearchParameter(Uri definitionUri)
        {
            SearchParameterInfo parameter = _inner.GetSearchParameter(definitionUri);

            if (parameter.IsSearchable)
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
        /// This method is not really useful, as the parameters returned are already specified
        /// to be supported and searchable
        /// </summary>
        /// <param name="isSupported">Overidden to be true.</param>
        /// <param name="isSearchable">Overridden to be true.</param>
        /// <returns>Returns all search parameters that are both supported and searchable</returns>
        public IEnumerable<SearchParameterInfo> GetSearchByStatus(bool isSupported, bool isSearchable)
        {
            return _inner.GetSearchByStatus(true, true);
        }
    }
}

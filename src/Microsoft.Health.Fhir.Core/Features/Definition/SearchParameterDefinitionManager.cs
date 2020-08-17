// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    /// <summary>
    /// Provides mechanism to access search parameter definition.
    /// </summary>
    public class SearchParameterDefinitionManager : ISearchParameterDefinitionManager, IStartable
    {
        private readonly IModelInfoProvider _modelInfoProvider;

        private IDictionary<string, IDictionary<string, SearchParameterInfo>> _typeLookup;
        private bool _started;

        public SearchParameterDefinitionManager(IModelInfoProvider modelInfoProvider)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));

            _modelInfoProvider = modelInfoProvider;
        }

        internal IDictionary<Uri, SearchParameterInfo> UrlLookup { get; set; }

        public IEnumerable<SearchParameterInfo> AllSearchParameters => UrlLookup.Values;

        public string SearchParametersHash { get; set; }

        public void Start()
        {
            // This method is idempotent because dependent Start methods are not guaranteed to be executed in order.
            if (!_started)
            {
                var builder = new SearchParameterDefinitionBuilder(
                    _modelInfoProvider,
                    "search-parameters.json");

                builder.Build();

                _typeLookup = builder.ResourceTypeDictionary;
                UrlLookup = builder.UriDictionary;

                List<string> list = UrlLookup.Values.Where(p => p.Type == ValueSets.SearchParamType.Composite).Select(p => string.Join("|", p.Component.Select(c => UrlLookup[c.DefinitionUrl].Type))).Distinct().ToList();

                _started = true;
            }
        }

        public IEnumerable<SearchParameterInfo> GetSearchParameters(string resourceType)
        {
            if (_typeLookup.TryGetValue(resourceType, out IDictionary<string, SearchParameterInfo> value))
            {
                return value.Values;
            }

            throw new ResourceNotSupportedException(resourceType);
        }

        public SearchParameterInfo GetSearchParameter(string resourceType, string name)
        {
            if (_typeLookup.TryGetValue(resourceType, out IDictionary<string, SearchParameterInfo> lookup) &&
                lookup.TryGetValue(name, out SearchParameterInfo searchParameter))
            {
                return searchParameter;
            }

            throw new SearchParameterNotSupportedException(resourceType, name);
        }

        public bool TryGetSearchParameter(string resourceType, string name, out SearchParameterInfo searchParameter)
        {
            searchParameter = null;

            return _typeLookup.TryGetValue(resourceType, out IDictionary<string, SearchParameterInfo> searchParameters) &&
                searchParameters.TryGetValue(name, out searchParameter);
        }

        public SearchParameterInfo GetSearchParameter(Uri definitionUri)
        {
            if (UrlLookup.TryGetValue(definitionUri, out SearchParameterInfo value))
            {
                return value;
            }

            throw new SearchParameterNotSupportedException(definitionUri);
        }

        public ValueSets.SearchParamType GetSearchParameterType(SearchParameterInfo searchParameter, int? componentIndex)
        {
            if (componentIndex == null)
            {
                return searchParameter.Type;
            }

            SearchParameterComponentInfo component = searchParameter.Component[componentIndex.Value];
            SearchParameterInfo componentSearchParameter = GetSearchParameter(component.DefinitionUrl);

            return componentSearchParameter.Type;
        }
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Serialization;
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
        private readonly FhirJsonParser _fhirJsonParser;
        private readonly IModelInfoProvider _modelInfoProvider;

        private IDictionary<string, IDictionary<string, SearchParameterInfo>> _typeLookup;

        public SearchParameterDefinitionManager(FhirJsonParser fhirJsonParser, IModelInfoProvider modelInfoProvider)
        {
            EnsureArg.IsNotNull(fhirJsonParser, nameof(fhirJsonParser));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));

            _fhirJsonParser = fhirJsonParser;
            _modelInfoProvider = modelInfoProvider;
        }

        internal IDictionary<Uri, SearchParameterInfo> UrlLookup { get; set; }

        public IEnumerable<SearchParameterInfo> AllSearchParameters => UrlLookup.Values;

        public void Start()
        {
            Type type = GetType();

            var builder = new SearchParameterDefinitionBuilder(
                _fhirJsonParser,
                _modelInfoProvider,
                type.Assembly,
                $"{type.Namespace}.search-parameters.json");

            builder.Build();

            _typeLookup = builder.ResourceTypeDictionary;
            UrlLookup = builder.UriDictionary;

            List<string> list = UrlLookup.Values.Where(p => p.Type == ValueSets.SearchParamType.Composite).Select(p => string.Join("|", p.Component.Select(c => UrlLookup[c.DefinitionUrl].Type))).Distinct().ToList();
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

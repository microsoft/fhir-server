// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    /// <summary>
    /// Provides mechanism to access search parameter definition.
    /// </summary>
    public class SearchParameterDefinitionManager : ISearchParameterDefinitionManager, IStartable, IProvideCapability
    {
        private readonly FhirJsonParser _fhirJsonParser;
        private readonly IModelInfoProvider _modelInfoProvider;

        private IDictionary<string, IDictionary<string, SearchParameterInfo>> _typeLookup;
        private IDictionary<Uri, SearchParameterInfo> _urlLookup;

        public SearchParameterDefinitionManager(FhirJsonParser fhirJsonParser, IModelInfoProvider modelInfoProvider)
        {
            EnsureArg.IsNotNull(fhirJsonParser, nameof(fhirJsonParser));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));

            _fhirJsonParser = fhirJsonParser;
            _modelInfoProvider = modelInfoProvider;
        }

        public IEnumerable<SearchParameterInfo> AllSearchParameters => _urlLookup.Values;

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
            _urlLookup = builder.UriDictionary;

            List<string> list = _urlLookup.Values.Where(p => p.Type == ValueSets.SearchParamType.Composite).Select(p => string.Join("|", p.Component.Select(c => _urlLookup[c.DefinitionUrl].Type))).Distinct().ToList();
        }

        public IEnumerable<SearchParameterInfo> GetSearchParameters(string resourceType)
        {
            if (_typeLookup.TryGetValue(resourceType, out IDictionary<string, SearchParameterInfo> value))
            {
                return value.Values;
            }

            throw new ResourceNotSupportedException(resourceType);
        }

        public bool TryGetSearchParameter(string resourceType, string name, out SearchParameterInfo searchParameter)
        {
            searchParameter = null;

            return _typeLookup.TryGetValue(resourceType, out IDictionary<string, SearchParameterInfo> searchParameters) &&
                searchParameters.TryGetValue(name, out searchParameter);
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

        public SearchParameterInfo GetSearchParameter(Uri definitionUri)
        {
            if (_urlLookup.TryGetValue(definitionUri, out SearchParameterInfo value))
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

        void IProvideCapability.Build(IListedCapabilityStatement statement)
        {
            EnsureArg.IsNotNull(statement, nameof(statement));

            foreach (KeyValuePair<string, IDictionary<string, SearchParameterInfo>> entry in _typeLookup)
            {
                var searchParameters = entry.Value.Select(
                        searchParameter => new CapabilityStatement.SearchParamComponent
                        {
                            Name = searchParameter.Key,
                            Type = Enum.Parse<SearchParamType>(searchParameter.Value.Type.ToString()),
                        });

                var capabilityStatement = (ListedCapabilityStatement)statement;

                var resourceType = Enum.Parse<ResourceType>(entry.Key);

                capabilityStatement.TryAddSearchParams(resourceType, searchParameters);
                capabilityStatement.TryAddRestInteraction(resourceType, CapabilityStatement.TypeRestfulInteraction.SearchType);
            }
        }
    }
}

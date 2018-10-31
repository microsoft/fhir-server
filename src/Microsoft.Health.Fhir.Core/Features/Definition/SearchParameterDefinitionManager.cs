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

namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    /// <summary>
    /// Provides mechanism to access search parameter definition.
    /// </summary>
    public class SearchParameterDefinitionManager : ISearchParameterDefinitionManager, IStartable, IProvideCapability
    {
        private readonly FhirJsonParser _fhirJsonParser;

        private IDictionary<ResourceType, IDictionary<string, SearchParameter>> _typeLookup;
        private IDictionary<Uri, SearchParameter> _urlLookup;

        public SearchParameterDefinitionManager(FhirJsonParser fhirJsonParser)
        {
            EnsureArg.IsNotNull(fhirJsonParser, nameof(fhirJsonParser));

            _fhirJsonParser = fhirJsonParser;
        }

        public IEnumerable<SearchParameter> AllSearchParameters => _urlLookup.Values;

        public void Start()
        {
            Type type = GetType();

            var builder = new SearchParameterDefinitionBuilder(
                _fhirJsonParser,
                type.Assembly,
                $"{type.Namespace}.search-parameters.json");

            builder.Build();

            _typeLookup = builder.ResourceTypeDictionary;
            _urlLookup = builder.UriDictionary;
        }

        public IEnumerable<SearchParameter> GetSearchParameters(ResourceType resourceType)
        {
            if (_typeLookup.TryGetValue(resourceType, out IDictionary<string, SearchParameter> value))
            {
                return value.Values;
            }

            throw new ResourceNotSupportedException(resourceType.ToString());
        }

        public bool TryGetSearchParameter(ResourceType resourceType, string name, out SearchParameter searchParameter)
        {
            searchParameter = null;

            return _typeLookup.TryGetValue(resourceType, out IDictionary<string, SearchParameter> searchParameters) &&
                searchParameters.TryGetValue(name, out searchParameter);
        }

        public SearchParameter GetSearchParameter(ResourceType resourceType, string name)
        {
            if (_typeLookup.TryGetValue(resourceType, out IDictionary<string, SearchParameter> lookup) &&
                lookup.TryGetValue(name, out SearchParameter searchParameter))
            {
                return searchParameter;
            }

            throw new SearchParameterNotSupportedException(resourceType, name);
        }

        public SearchParameter GetSearchParameter(Uri definitionUri)
        {
            if (_urlLookup.TryGetValue(definitionUri, out SearchParameter value))
            {
                return value;
            }

            throw new SearchParameterNotSupportedException(definitionUri);
        }

        void IProvideCapability.Build(ListedCapabilityStatement statement)
        {
            EnsureArg.IsNotNull(statement, nameof(statement));

            foreach (KeyValuePair<ResourceType, IDictionary<string, SearchParameter>> entry in _typeLookup)
            {
                IEnumerable<CapabilityStatement.SearchParamComponent> searchParameters = entry.Value.Select(
                        searchParameter => new CapabilityStatement.SearchParamComponent
                        {
                            Name = searchParameter.Key,
                            Type = searchParameter.Value.Type,
                        });

                statement.TryAddSearchParams(entry.Key, searchParameters);
                statement.TryAddRestInteraction(entry.Key, CapabilityStatement.TypeRestfulInteraction.SearchType);
            }
        }
    }
}

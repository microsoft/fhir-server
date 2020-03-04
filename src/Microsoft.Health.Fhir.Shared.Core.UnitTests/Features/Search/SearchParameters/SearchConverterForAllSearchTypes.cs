// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.FhirPath.Expressions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    public class SearchConverterForAllSearchTypes : IClassFixture<SearchParameterFixtureData>
    {
        private readonly ITestOutputHelper _outputHelper;
        private readonly SearchParameterFixtureData _fixtureData;

        public SearchConverterForAllSearchTypes(ITestOutputHelper outputHelper, SearchParameterFixtureData fixtureData)
        {
            _outputHelper = outputHelper;
            _fixtureData = fixtureData;
        }

        [Theory]
        [MemberData(nameof(GetAllSearchParameters))]
        public void CheckSearchParameter(
            string resourceType,
            IEnumerable<SearchParameterInfo> parameters)
        {
            SearchParameterToTypeResolver.Log = s => _outputHelper.WriteLine(s);

            foreach (var parameterInfo in parameters)
            {
                var fhirPath = parameterInfo.Expression;
                var parameterName = parameterInfo.Name;
                var searchParamType = parameterInfo.Type;

                _outputHelper.WriteLine("** Evaluating: " + fhirPath);

                var converters =
                    GetConvertsForSearchParameters(resourceType, parameterInfo);

                Assert.True(
                    converters.Any(x => x.hasConverter),
                    $"{parameterName} ({resourceType}) was not able to be mapped.");

                string listedTypes = string.Join(",", converters.Select(x => x.result.ClassMapping.NativeType.Name));
                _outputHelper.WriteLine($"Info: {parameterName} ({searchParamType}) found {listedTypes} types ({converters.Count}).");

                foreach (var result in converters.Where(x => x.hasConverter || !parameterInfo.IsPartiallySupported))
                {
                    var found = _fixtureData.Manager.TryGetConverter(result.result.ClassMapping.NativeType, SearchIndexer.GetSearchValueTypeForSearchParamType(result.result.SearchParamType), out var converter);

                    var converterText = found ? converter.GetType().Name : "None";
                    string searchTermMapping = $"Search term '{parameterName}' ({result.result.SearchParamType}) mapped to '{result.result.ClassMapping.NativeType.Name}', converter: {converterText}";
                    _outputHelper.WriteLine(searchTermMapping);

                    Assert.True(found, searchTermMapping);
                }
            }
        }

        [Fact]
        public void ListAllUnsupportedTypes()
        {
            var unsupported = new UnsupportedSearchParameters();

            SearchParameterDefinitionManager manager = SearchParameterFixtureData.CreateSearchParameterDefinitionManager();

            var resourceAndSearchParameters = ModelInfoProvider.Instance
                .GetResourceTypeNames()
                .Select(resourceType => (resourceType, parameters: manager.GetSearchParameters(resourceType)));

            foreach (var searchParameterRow in resourceAndSearchParameters)
            {
                foreach (SearchParameterInfo parameterInfo in searchParameterRow.parameters)
                {
                    if (parameterInfo.Name != "_type")
                    {
                        var converters = GetConvertsForSearchParameters(searchParameterRow.resourceType, parameterInfo);

                        if (converters.All(x => x.hasConverter == false))
                        {
                            unsupported.Unsupported.Add(parameterInfo.Url);
                        }
                        else if (converters.Any(x => x.hasConverter == false))
                        {
                            unsupported.PartialSupport.Add(parameterInfo.Url);
                        }
                    }
                }
            }

            // Print the current state to the console
            _outputHelper.WriteLine(JsonConvert.SerializeObject(unsupported, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Formatting.Indented,
            }));

            // Check this against the list already in the system:
            var systemUnsupported = new UnsupportedSearchParameters();
            foreach (var searchParameter in resourceAndSearchParameters.SelectMany(x => x.parameters))
            {
                if (!searchParameter.IsSupported)
                {
                    systemUnsupported.Unsupported.Add(searchParameter.Url);
                }
                else if (searchParameter.IsPartiallySupported)
                {
                    systemUnsupported.PartialSupport.Add(searchParameter.Url);
                }
            }

            // Expect that the static file "unsupported-search-parameters.json" equals the generated list
            Assert.Equal(systemUnsupported.Unsupported, unsupported.Unsupported);
            Assert.Equal(systemUnsupported.PartialSupport, unsupported.PartialSupport);
        }

        private IReadOnlyCollection<(SearchParameterTypeResult result, bool hasConverter, IFhirElementToSearchValueTypeConverter converter)> GetConvertsForSearchParameters(
            string resourceType,
            SearchParameterInfo parameterInfo)
        {
            var parsed = _fixtureData.Compiler.Parse(parameterInfo.Expression);

            (SearchParamType Type, Expression, Uri DefinitionUrl)[] componentExpressions = parameterInfo.Component
                .Select(x => (_fixtureData.SearchDefinitionManager.UrlLookup[x.DefinitionUrl].Type,
                    _fixtureData.Compiler.Parse(x.Expression),
                    x.DefinitionUrl))
                .ToArray();

            SearchParameterTypeResult[] results = SearchParameterToTypeResolver.Resolve(
                resourceType,
                (parameterInfo.Type, parsed, parameterInfo.Url),
                componentExpressions).ToArray();

            var converters = results
                .Select(result => (
                    result,
                    hasConverter: _fixtureData.Manager.TryGetConverter(
                        result.ClassMapping.NativeType,
                        SearchIndexer.GetSearchValueTypeForSearchParamType(result.SearchParamType),
                        out IFhirElementToSearchValueTypeConverter converter),
                    converter))
                .ToArray();

            return converters;
        }

        public static IEnumerable<object[]> GetAllSearchParameters()
        {
            var manager = SearchParameterFixtureData.CreateSearchParameterDefinitionManager();

            var values = ModelInfoProvider.Instance
                .GetResourceTypeNames()
                .Select(resourceType => (resourceType, parameters: manager.GetSearchParameters(resourceType)));

            foreach ((string resourceType, IEnumerable<SearchParameterInfo> parameters) row in values)
            {
                yield return new object[] { row.resourceType, row.parameters.Where(x => x.Name != "_type" && x.IsSupported) };
            }
        }
    }
}

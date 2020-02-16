// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Models;
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
            string parameterName,
            Microsoft.Health.Fhir.ValueSets.SearchParamType searchParamType,
            string fhirPath,
            SearchParameterInfo parameterInfo)
        {
            SearchParameterToTypeResolver.Log = s => _outputHelper.WriteLine(s);

            _outputHelper.WriteLine("** Evaluating: " + fhirPath);

            var parsed = _fixtureData.Compiler.Parse(fhirPath);

            var componentExpressions = parameterInfo.Component
                .Select(x => (_fixtureData.SearchDefinitionManager.UrlLookup[x.DefinitionUrl].Type, _fixtureData.Compiler.Parse(x.Expression)))
                .ToArray();

            var results = SearchParameterToTypeResolver.Resolve(
                resourceType,
                (searchParamType, parsed),
                componentExpressions).ToArray();

            Assert.True(results.Any(), $"{parameterName} ({resourceType}) was not able to be mapped.");

            string listedTypes = string.Join(",", results.Select(x => x.ClassMapping.NativeType.Name));
            _outputHelper.WriteLine($"Info: {parameterName} ({searchParamType}) found {results.Length} types ({listedTypes}).");

            foreach (var result in results)
            {
                var found = _fixtureData.Manager.TryGetConverter(result.ClassMapping.NativeType, SearchIndexer.GetSearchValueTypeForSearchParamType(result.SearchParamType), out var converter);

                var converterText = found ? converter.GetType().Name : "None";
                string searchTermMapping = $"Search term '{parameterName}' ({result.SearchParamType}) mapped to '{result.ClassMapping.NativeType.Name}', converter: {converterText}";
                _outputHelper.WriteLine(searchTermMapping);

                Assert.True(
                    found,
                    searchTermMapping);
            }
        }

        [Fact]
        public void ListAllUnsupportedTypes()
        {
            var unsupportedList = new List<UnsupportedSearchParameters>();

            var manager = SearchParameterFixtureData.CreateSearchParameterDefinitionManager();

            var values = ModelInfoProvider.Instance
                .GetResourceTypeNames()
                .Select(resourceType => (resourceType, manager.GetSearchParameters(resourceType)));

            foreach (var searchParameterRow in values)
            {
                var unsupported = new UnsupportedSearchParameters();
                unsupported.Resource = searchParameterRow.resourceType;

                foreach (var parameterInfo in searchParameterRow.Item2)
                {
                    if (parameterInfo.Name != "_type")
                    {
                        var parsed = _fixtureData.Compiler.Parse(parameterInfo.Expression);

                        var componentExpressions = parameterInfo.Component
                            .Select(x => (_fixtureData.SearchDefinitionManager.UrlLookup[x.DefinitionUrl].Type,
                                _fixtureData.Compiler.Parse(x.Expression)))
                            .ToArray();

                        var results = SearchParameterToTypeResolver.Resolve(
                            searchParameterRow.resourceType,
                            (parameterInfo.Type, parsed),
                            componentExpressions).ToArray();

                        var converters = results
                            .Select(result => new
                            {
                                result,
                                hasConverter = _fixtureData.Manager.TryGetConverter(
                                    result.ClassMapping.NativeType,
                                    SearchIndexer.GetSearchValueTypeForSearchParamType(result.SearchParamType),
                                    out IFhirElementToSearchValueTypeConverter converter),
                                converter,
                            })
                            .ToArray();

                        if (converters.All(x => x.hasConverter == false))
                        {
                            unsupported.Unsupported.Add(parameterInfo.Name);
                        }
                        else if (converters.Any(x => x.hasConverter == false))
                        {
                            unsupported.PartialSupport.Add(parameterInfo.Name);
                        }
                    }
                }

                if (unsupported.Unsupported.Any() || unsupported.PartialSupport.Any())
                {
                    unsupportedList.Add(unsupported);
                }
            }

            File.WriteAllText("/Users/bkowitz/src/unsupported-search-parameters-stu3.json", JsonConvert.SerializeObject(unsupportedList, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
            }));
        }

        public static IEnumerable<object[]> GetAllSearchParameters()
        {
            var manager = SearchParameterFixtureData.CreateSearchParameterDefinitionManager();

            var values = ModelInfoProvider.Instance
                .GetResourceTypeNames()
                .Select(resourceType => (resourceType, manager.GetSearchParameters(resourceType)));

            foreach (var row in values)
            {
                foreach (var p in row.Item2)
                {
                    if (p.Name != "_type")
                    {
                        yield return new object[] { row.resourceType, p.Name, p.Type, p.Expression, p };
                    }
                }
            }
        }

        private class UnsupportedSearchParameters
        {
            public string Resource { get; set; }

            public SortedSet<string> Unsupported { get; set; } = new SortedSet<string>();

            public SortedSet<string> PartialSupport { get; set; } = new SortedSet<string>();
        }
    }
}

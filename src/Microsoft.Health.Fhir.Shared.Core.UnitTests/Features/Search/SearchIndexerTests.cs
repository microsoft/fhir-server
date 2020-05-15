// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    public class SearchIndexerTests : IClassFixture<SearchParameterFixtureData>
    {
        private readonly SearchParameterFixtureData _fixtureData;
        private ISearchIndexer _indexer;

        private JsonSerializerSettings _settings = new JsonSerializerSettings
        {
            Converters = new List<JsonConverter>
            {
                new StringEnumConverter(),
            },
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
        };

        public SearchIndexerTests(SearchParameterFixtureData fixtureData)
        {
            _fixtureData = fixtureData;

            _indexer = new SearchIndexer(
                () => _fixtureData.SearchDefinitionManager,
                SearchParameterFixtureData.Manager,
                new LightweightReferenceToElementResolver(new ReferenceSearchValueParser(new FhirRequestContextAccessor()), ModelInfoProvider.Instance),
                NullLogger<SearchIndexer>.Instance);
        }

        [Theory]
        [InlineData("DocumentReference-example")]
        [InlineData("DocumentReference-example-002")]
        [InlineData("DocumentReference-example-003")]
        public void GivenAResource_WhenExtractingValues_ThenTheCorrectValuesAreReturned(string resourceFile)
        {
            var document = Samples.GetJsonSample<DocumentReference>(resourceFile).ToResourceElement();
            var indexDocument = Samples.GetJson($"{resourceFile}.indexes");

            var indexes = _indexer.Extract(document)
                .Select(x => new { x.SearchParameter.Name, x.SearchParameter.Type, x.Value })
                .OrderBy(x => x.Name)
                .ToArray();

            var asJson = JsonConvert.SerializeObject(indexes, Formatting.Indented, _settings);

            Assert.Equal(indexDocument, asJson);
        }
    }
}

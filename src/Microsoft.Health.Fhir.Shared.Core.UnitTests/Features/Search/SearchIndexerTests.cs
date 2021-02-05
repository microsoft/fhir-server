// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    public class SearchIndexerTests : IClassFixture<SearchParameterFixtureData>, IAsyncLifetime
    {
        private readonly SearchParameterFixtureData _fixture;
        private ISearchIndexer _indexer;

        private readonly JsonSerializerSettings _settings = new JsonSerializerSettings
        {
            Converters = new List<JsonConverter>
            {
                new StringEnumConverter(),
            },
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
        };

        public SearchIndexerTests(SearchParameterFixtureData fixture) => _fixture = fixture;

        public async Task InitializeAsync()
        {
            _indexer = new TypedElementSearchIndexer(
                await _fixture.GetSupportedSearchDefinitionManagerAsync(),
                await SearchParameterFixtureData.GetFhirNodeToSearchValueTypeConverterManagerAsync(),
                new LightweightReferenceToElementResolver(new ReferenceSearchValueParser(new FhirRequestContextAccessor()), ModelInfoProvider.Instance),
                ModelInfoProvider.Instance,
                NullLogger<SearchIndexer>.Instance);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Theory]
        [InlineData("DocumentReference-example-relatesTo-code-appends")]
        [InlineData("DocumentReference-example-relatesTo-code-transforms-replaces-target")]
        [InlineData("DocumentReference-example-relatesTo-code-transforms")]
        public void GivenAResource_WhenExtractingValues_ThenTheCorrectValuesAreReturned(string resourceFile)
        {
            var document = Samples.GetJsonSample<DocumentReference>(resourceFile).ToResourceElement();
            var indexDocument = Samples.GetJson($"{resourceFile}.indexes");

            var indices = _indexer.Extract(document)
                .Select(x => new { x.SearchParameter.Code, x.SearchParameter.Type, x.Value })
                .OrderBy(x => x.Code)
                .ToArray();

            var extractedIndices = new List<JToken>();
            foreach (var index in indices)
            {
                extractedIndices.Add(JToken.Parse(JsonConvert.SerializeObject(index, Formatting.Indented, _settings)));
            }

            var expectedJObjects = JArray.Parse(indexDocument);

            for (var i = 0; i < expectedJObjects.Count; i++)
            {
                Assert.True(JToken.DeepEquals(expectedJObjects[i], extractedIndices[i]), "Expected: " + Environment.NewLine + JsonConvert.SerializeObject(indices, Formatting.Indented, _settings));
            }
        }
    }
}

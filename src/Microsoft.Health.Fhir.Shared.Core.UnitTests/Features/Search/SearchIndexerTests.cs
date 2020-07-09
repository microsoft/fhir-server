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
    public class SearchIndexerTests
    {
        private ISearchIndexer _indexer;

        private JsonSerializerSettings _settings = new JsonSerializerSettings
        {
            Converters = new List<JsonConverter>
            {
                new StringEnumConverter(),
            },
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
        };

        public SearchIndexerTests()
        {
            _indexer = new TypedElementSearchIndexer(
                SearchParameterFixtureData.SupportedSearchDefinitionManager,
                SearchParameterFixtureData.Manager,
                new LightweightReferenceToElementResolver(new ReferenceSearchValueParser(new FhirRequestContextAccessor()), ModelInfoProvider.Instance),
                ModelInfoProvider.Instance,
                NullLogger<SearchIndexer>.Instance);
        }

        [Theory]
        [InlineData("DocumentReference-example-relatesTo-code-appends")]
        [InlineData("DocumentReference-example-relatesTo-code-transforms-replaces-target")]
        [InlineData("DocumentReference-example-relatesTo-code-transforms")]
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

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.CosmosDb.Features.Search;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage
{
    /// <summary>
    /// Pins down what the serializer the Cosmos DB client is configured with can and cannot read back, because
    /// the answer decides which persistence primitives the data store is allowed to use.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class FhirCosmosResourceWrapperSerializationTests
    {
        private readonly CosmosSerializer _serializer =
            new FhirCosmosClientInitializer.FhirCosmosSerializer(NullLogger<FhirCosmosClientInitializer>.Instance);

        [Fact]
        public void GivenAStoredDocument_WhenItIsReadBackAndWrittenOutUnchanged_ThenItsSearchIndicesAreDestroyed()
        {
            // The search indices are write-only: SearchIndexEntryConverter.ReadJson returns null for every
            // entry it is handed, so nothing that was read back can be written back without losing them.
            JObject storedDocument = SerializeToDocument(CreateWrapper());

            var storedSearchIndices = (JArray)storedDocument[KnownResourceWrapperProperties.SearchIndices];
            Assert.NotEmpty(storedSearchIndices);
            Assert.All(storedSearchIndices, entry => Assert.NotNull(entry[SearchValueConstants.ParamName]));

            FhirCosmosResourceWrapper readBack = Deserialize(storedDocument);
            JObject rewrittenDocument = SerializeToDocument(readBack);

            var rewrittenSearchIndices = (JArray)rewrittenDocument[KnownResourceWrapperProperties.SearchIndices];
            Assert.NotEmpty(rewrittenSearchIndices);
            Assert.All(rewrittenSearchIndices, entry => Assert.Equal(JTokenType.Null, entry.Type));

            // Persisting a wrapper that came from a read therefore silently strips the resource out of every
            // search index it belongs to. Any write the data store issues to confirm a precondition must leave
            // the stored document alone instead of re-persisting what it read.
            Assert.NotEqual(storedSearchIndices.ToString(Formatting.None), rewrittenSearchIndices.ToString(Formatting.None));
        }

        [Fact]
        public void GivenAStoredDocumentWithoutAVersionProperty_WhenItIsReadBack_ThenItsVersionIsTakenFromTheETag()
        {
            // A resource that has never been updated is stored without a "version" property, so its FHIR
            // version is whatever its _etag happens to be. Every write assigns a new _etag, which means a write
            // against such a document changes the version the resource reports to clients.
            JObject storedDocument = SerializeToDocument(CreateWrapper());
            storedDocument.Remove(KnownResourceWrapperProperties.Version);
            storedDocument["_etag"] = "\"0000d986-0000-0700-0000-5f9d1b7e0000\"";

            FhirCosmosResourceWrapper readBack = Deserialize(storedDocument);

            Assert.Equal("0000d986-0000-0700-0000-5f9d1b7e0000", readBack.Version);
        }

        private static FhirCosmosResourceWrapper CreateWrapper()
        {
            var rawResourceFactory = new RawResourceFactory(new FhirJsonSerializer());
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Id = "serialization-round-trip";
            observation.VersionId = "1";
            ResourceElement typedElement = observation.ToResourceElement();

            var wrapper = new ResourceWrapper(
                typedElement,
                rawResourceFactory.Create(typedElement, keepMeta: true, keepVersion: true),
                new ResourceRequest(HttpMethod.Post, "http://fhir"),
                deleted: false,
                searchIndices: null,
                compartmentIndices: null,
                lastModifiedClaims: null);

            wrapper.SearchIndices = new List<SearchIndexEntry>
            {
                new SearchIndexEntry(new SearchParameterInfo("code", "code"), new StringSearchValue("body-weight")),
                new SearchIndexEntry(new SearchParameterInfo("value-quantity", "value-quantity"), new NumberSearchValue(67)),
            };

            return new FhirCosmosResourceWrapper(wrapper);
        }

        private JObject SerializeToDocument(FhirCosmosResourceWrapper wrapper)
        {
            using Stream stream = _serializer.ToStream(wrapper);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return JObject.Parse(reader.ReadToEnd());
        }

        private FhirCosmosResourceWrapper Deserialize(JObject document)
        {
            return _serializer.FromStream<FhirCosmosResourceWrapper>(
                new MemoryStream(Encoding.UTF8.GetBytes(document.ToString(Formatting.None))));
        }
    }
}

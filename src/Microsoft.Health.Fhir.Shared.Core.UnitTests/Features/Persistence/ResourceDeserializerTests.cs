// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Persistence
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Serialization)]
    public class ResourceDeserializerTests
    {
        private readonly RawResourceFactory _rawResourceFactory;

        public ResourceDeserializerTests()
        {
            _rawResourceFactory = new RawResourceFactory(new FhirJsonSerializer());
        }

        [Fact]
        public void GivenARawResourceOfUnknownType_WhenDeserializing_ThenANotSupportedExceptionIsThrown()
        {
            var raw = new RawResource("{}", FhirResourceFormat.Unknown, isMetaSet: false);
            var wrapper = new ResourceWrapper("id1", "version1", "Observation", raw, new ResourceRequest(HttpMethod.Post, "http://fhir"), Clock.UtcNow, false, null, null, null);

            Assert.Throws<NotSupportedException>(() => Deserializers.ResourceDeserializer.Deserialize(wrapper));
        }

        [Fact]
        public void GivenARawResource_WhenDeserializingFromJson_ThenTheObjectIsReturned()
        {
            var observation = Samples.GetDefaultObservation()
                .UpdateId("id1");

            var wrapper = new ResourceWrapper(observation, _rawResourceFactory.Create(observation, keepMeta: true), new ResourceRequest(HttpMethod.Post, "http://fhir"), false, null, null, null);

            var newObject = Deserializers.ResourceDeserializer.Deserialize(wrapper);

            Assert.Equal(observation.Id, newObject.Id);
            Assert.Equal(observation.VersionId, newObject.VersionId);
        }

        [Fact]
        public void GivenARawResourceFromFhirNet1_WhenDeserializingFromJson_ThenTheObjectIsReturned()
        {
            var oldValidResource = @"{
  ""resourceType"": ""Patient"",
  ""birthDate"": ""1991-02-03T11:22:33Z""
    }";

            var observation = Samples.GetDefaultPatient();

            var wrapper = new ResourceWrapper(observation, new RawResource(oldValidResource, FhirResourceFormat.Json, false), new ResourceRequest(HttpMethod.Post, "http://fhir"), false, null, null, null);

            var newObject = Deserializers.ResourceDeserializer.Deserialize(wrapper);
        }

        [Fact]
        public void GivenARawResourceFromFhirNet1WithMultipleDate_WhenDeserializingFromJson_ThenTheObjectIsReturned()
        {
            var oldValidResource = @"{
    ""resourceType"": ""Goal"",
    ""startDate"": ""1991-02-03T11:22:33Z"",
    ""target"": {
            ""dueDate"":""2002-01-03T02:00""
            }
    }";

            var observation = Samples.GetDefaultPatient();

            var wrapper = new ResourceWrapper(observation, new RawResource(oldValidResource, FhirResourceFormat.Json, false), new ResourceRequest(HttpMethod.Post, "http://fhir"), false, null, null, null);

            var newObject = Deserializers.ResourceDeserializer.Deserialize(wrapper);
        }

        [Fact]
        public void GivenABadResource_WhenDeserializingFromJson_ThenExceptionThrown()
        {
            var oldValidResource = @"{
  ""resourceType"": ""Patient"",
  ""birthDate"": ""1991-02-03"",
  ""mutlipleBirthBoolean"":""cat""
    }";

            var observation = Samples.GetDefaultPatient();

            var wrapper = new ResourceWrapper(observation, new RawResource(oldValidResource, FhirResourceFormat.Json, false), new ResourceRequest(HttpMethod.Post, "http://fhir"), false, null, null, null);

            Assert.Throws<StructuralTypeException>(() => Deserializers.ResourceDeserializer.Deserialize(wrapper));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenAResourceWrapper_WhenDeserializingToJsonDocumentAndVersionIdNotSet_UpdatedWithVersionIdFromResourceWrapper(bool pretty)
        {
            var patient = Samples.GetDefaultPatient().UpdateVersion("3").UpdateLastUpdated(Clock.UtcNow - TimeSpan.FromDays(30));

            var wrapper = new ResourceWrapper(patient, _rawResourceFactory.Create(patient, keepMeta: false), new ResourceRequest(HttpMethod.Post, "http://fhir"), false, null, null, null);
            wrapper.Version = "2";

            var rawString = await SerializeToJsonString(new RawResourceElement(wrapper), pretty);
            Assert.NotNull(rawString);

            var deserialized = new FhirJsonParser(DefaultParserSettings.Settings).Parse<Patient>(rawString);

            Assert.Equal(wrapper.Version, deserialized.VersionId);
            Assert.Equal(wrapper.LastModified, deserialized.Meta.LastUpdated);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenAResourceWrapper_WhenDeserializingToJsonDocumentAndVersionIdSet_MaintainsVersionIdInRawResourceString(bool pretty)
        {
            var lastUpdated = Clock.UtcNow - TimeSpan.FromDays(30);
            var patient = Samples.GetDefaultPatient().UpdateVersion("3").UpdateLastUpdated(lastUpdated);

            var wrapper = new ResourceWrapper(patient, _rawResourceFactory.Create(patient, keepMeta: true), new ResourceRequest(HttpMethod.Post, "http://fhir"), false, null, null, null);
            wrapper.Version = "2";

            var rawString = await SerializeToJsonString(new RawResourceElement(wrapper), pretty);
            Assert.NotNull(rawString);

            var deserialized = new FhirJsonParser(DefaultParserSettings.Settings).Parse<Patient>(rawString);

            Assert.NotEqual(wrapper.Version, deserialized.VersionId);
            Assert.Equal(lastUpdated, deserialized.Meta.LastUpdated);
        }

        private async Task<string> SerializeToJsonString(RawResourceElement rawResourceElement, bool pretty)
        {
            using (var ms = new MemoryStream())
            using (var sr = new StreamReader(ms))
            {
                await rawResourceElement.SerializeToStreamAsUtf8Json(ms, pretty);
                ms.Seek(0, SeekOrigin.Begin);
                return await sr.ReadToEndAsync();
            }
        }
    }
}

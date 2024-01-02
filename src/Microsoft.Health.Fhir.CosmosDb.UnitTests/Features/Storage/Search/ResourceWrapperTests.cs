// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ResourceWrapperTests
    {
        private readonly RawResourceFactory _rawResourceFactory;

        public ResourceWrapperTests()
        {
            _rawResourceFactory = new RawResourceFactory(new FhirJsonSerializer());
        }

        [Fact]
        public void GivenAResourceWrapper_WhenGettingVersion_TheETagShouldBeUsedWhenVersionIsEmpty()
        {
            var wrapper = Samples.GetJsonSample<FhirCosmosResourceWrapper>("ResourceWrapperNoVersion");
            Assert.Equal("00002804-0000-0000-0000-59f272c60000", wrapper.Version);
        }

        [Fact]
        public void GivenAResourceWrapper_WhenConvertingToAHistoryObject_ThenTheCorrectPropertiesAreUpdated()
        {
            var wrapper = Samples.GetJsonSample<FhirCosmosResourceWrapper>("ResourceWrapperNoVersion");

            var id = wrapper.Id;
            var lastModified = new DateTimeOffset(2017, 1, 1, 1, 1, 1, TimeSpan.Zero);

            var historyRecord = new FhirCosmosResourceWrapper(
                id,
                "version1",
                wrapper.ResourceTypeName,
                wrapper.RawResource,
                wrapper.Request,
                lastModified,
                wrapper.IsDeleted,
                true,
                null,
                null,
                null);

            Assert.Equal($"{id}_{historyRecord.Version}", historyRecord.Id);
            Assert.Equal(lastModified, historyRecord.LastModified);
            Assert.True(historyRecord.IsHistory);
            Assert.Equal("version1", historyRecord.Version);
        }

#if NET8_0_OR_GREATER
        [Fact]
        public void GivenAResource_WhenCreatingAResourceWrapper_ThenMetaPropertiesAreCorrect()
        {
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Id = "id1";
            observation.VersionId = "version1";
            observation.Meta.Profile = new List<string> { "test" };

            var lastModified = new DateTimeOffset(2017, 1, 1, 1, 1, 1, TimeSpan.Zero);
            using (Mock.Property(() => ClockResolver.TimeProvider, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(lastModified)))
            {
                ResourceElement typedElement = observation.ToResourceElement();

                var wrapper = new ResourceWrapper(typedElement, _rawResourceFactory.Create(typedElement, keepMeta: true), new ResourceRequest(HttpMethod.Post, "http://fhir"), false, null, null, null);
                var resource = Deserializers.ResourceDeserializer.Deserialize(wrapper);

                var poco = resource.ToPoco<Observation>();

                Assert.Equal(observation.VersionId, poco.Meta.VersionId);
                Assert.Equal(lastModified, poco.Meta.LastUpdated);
                Assert.Equal("test", poco.Meta.Profile.First());
            }
        }
#endif
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage
{
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
            var wrapper = Samples.GetJsonSample<CosmosResourceWrapper>("ResourceWrapperNoVersion");
            Assert.Equal("00002804-0000-0000-0000-59f272c60000", wrapper.Version);
        }

        [Fact]
        public void GivenAResourceWrapper_WhenConvertingToAHistoryObject_ThenTheCorrectPropertiesAreUpdated()
        {
            var wrapper = Samples.GetJsonSample<CosmosResourceWrapper>("ResourceWrapperNoVersion");

            var id = wrapper.Id;
            var lastModified = new DateTimeOffset(2017, 1, 1, 1, 1, 1, TimeSpan.Zero);

            var historyRecord = new CosmosResourceWrapper(
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

        [Fact]
        public void GivenAResource_WhenCreatingAResourceWrapper_ThenMetaPropertiesAreCorrect()
        {
            var observation = Samples.GetDefaultObservation();
            observation.Id = "id1";
            observation.VersionId = "version1";
            observation.Meta.Profile = new List<string> { "test" };

            var lastModified = new DateTimeOffset(2017, 1, 1, 1, 1, 1, TimeSpan.Zero);
            using (Mock.Property(() => Clock.UtcNowFunc, () => lastModified))
            {
                var wrapper = new ResourceWrapper(observation, _rawResourceFactory.Create(observation), new ResourceRequest("http://fhir", HttpMethod.Post), false, null, null, null);
                var resource = ResourceDeserializer.Deserialize(wrapper);

                Assert.Equal(observation.VersionId, resource.Meta.VersionId);
                Assert.Equal(lastModified, resource.Meta.LastUpdated);
                Assert.Equal("test", resource.Meta.Profile.First());
            }
        }
    }
}

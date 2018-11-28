// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net.Http;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Tests.Common;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Persistence
{
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
            var raw = new RawResource("{}", ResourceFormat.Unknown);
            var wrapper = new ResourceWrapper("id1", "version1", "Observation", raw, new ResourceRequest("http://fhir", HttpMethod.Post), Clock.UtcNow, false, null, null, null);

            Assert.Throws<NotSupportedException>(() => ResourceDeserializer.Deserialize(wrapper));
        }

        [Fact]
        public void GivenARawResource_WhenDeserializingFromJson_ThenTheObjectIsReturned()
        {
            var observation = Samples.GetDefaultObservation();
            observation.Id = "id1";
            var wrapper = new ResourceWrapper(observation, _rawResourceFactory.Create(observation), new ResourceRequest("http://fhir", HttpMethod.Post), false, null, null, null);

            var newObject = ResourceDeserializer.Deserialize(wrapper);

            Assert.Equal(observation.Id, newObject.Id);
            Assert.Equal(observation.VersionId, newObject.VersionId);
        }
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
{
    public class ResourceToNdjsonBytesSerializerTests
    {
        private readonly FhirJsonParser _jsonParser = new FhirJsonParser();
        private readonly FhirXmlParser _xmlParser = new FhirXmlParser();
        private readonly FhirJsonSerializer _jsonSerializer = new FhirJsonSerializer();

        private readonly ResourceToNdjsonBytesSerializer _serializer;

        private readonly Observation _resource;
        private readonly byte[] _expectedBytes;

        public ResourceToNdjsonBytesSerializerTests()
        {
            var resourceDeserializaer = new ResourceDeserializer(
                (FhirResourceFormat.Json, new Func<string, string, DateTimeOffset, ResourceElement>((str, version, lastModified) => _jsonParser.Parse<Resource>(str).ToResourceElement())),
                (FhirResourceFormat.Xml, new Func<string, string, DateTimeOffset, ResourceElement>((str, version, lastModified) => _xmlParser.Parse<Resource>(str).ToResourceElement())));

            _serializer = new ResourceToNdjsonBytesSerializer(resourceDeserializaer, _jsonSerializer);

            _resource = Samples.GetDefaultObservation().ToPoco<Observation>();
            _resource.Id = "test";

            string expectedString = $"{new FhirJsonSerializer().SerializeToString(_resource)}\n";

            _expectedBytes = Encoding.UTF8.GetBytes(expectedString);
        }

        [Fact]
        public void GivenARawResourceInJsonFormat_WhenSerialized_ThenCorrectByteArrayShouldBeProduced()
        {
            var rawResource = new RawResource(
                new FhirJsonSerializer().SerializeToString(_resource),
                FhirResourceFormat.Json);

            ResourceWrapper resourceWrapper = CreateResourceWrapper(rawResource);

            byte[] actualBytes = _serializer.Serialize(resourceWrapper);

            Assert.Equal(_expectedBytes, actualBytes);
        }

        [Fact]
        public void GivenARawResourceInXmlFormat_WhenSerialized_ThenCorrectByteArrayShouldBeProduced()
        {
            var rawResource = new RawResource(
                new FhirXmlSerializer().SerializeToString(_resource),
                FhirResourceFormat.Xml);

            ResourceWrapper resourceWrapper = CreateResourceWrapper(rawResource);

            byte[] actualBytes = _serializer.Serialize(resourceWrapper);

            Assert.Equal(_expectedBytes, actualBytes);
        }

        private ResourceWrapper CreateResourceWrapper(RawResource rawResource)
        {
            return new ResourceWrapper(
                _resource.ToResourceElement(),
                rawResource,
                null,
                false,
                null,
                null,
                null);
        }
    }
}

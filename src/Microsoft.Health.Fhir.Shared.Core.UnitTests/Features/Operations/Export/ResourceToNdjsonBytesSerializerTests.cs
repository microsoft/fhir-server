// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Text;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Export)]
    public class ResourceToNdjsonBytesSerializerTests
    {
        private readonly ResourceDeserializer _resourceDeserializaer;
        private readonly FhirJsonParser _jsonParser = new FhirJsonParser();
        private readonly FhirXmlParser _xmlParser = new FhirXmlParser();

        private readonly ResourceToNdjsonBytesSerializer _serializer;

        private readonly Observation _resource;
        private readonly byte[] _expectedBytes;

        public ResourceToNdjsonBytesSerializerTests()
        {
            _resourceDeserializaer = new ResourceDeserializer(
                (FhirResourceFormat.Json, new Func<string, string, DateTimeOffset, ResourceElement>((str, version, lastModified) => _jsonParser.Parse<Resource>(str).ToResourceElement())),
                (FhirResourceFormat.Xml, new Func<string, string, DateTimeOffset, ResourceElement>((str, version, lastModified) => _xmlParser.Parse<Resource>(str).ToResourceElement())));

            _serializer = new ResourceToNdjsonBytesSerializer();

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
                FhirResourceFormat.Json,
                isMetaSet: false);

            ResourceWrapper resourceWrapper = CreateResourceWrapper(rawResource);
            ResourceElement element = _resourceDeserializaer.DeserializeRaw(resourceWrapper.RawResource, resourceWrapper.Version, resourceWrapper.LastModified);

            byte[] actualBytes = _serializer.Serialize(element);

            Assert.Equal(_expectedBytes, actualBytes);
        }

        [Fact]
        public void GivenAInvalidElementNode_WhenSerialized_ByteArrayShouldBeProduced()
        {
            var node = ElementNode.FromElement(_resource.ToTypedElement());
            (((ScopedNode)node.Select("Observation.text").First()).Current as ElementNode).Value = "invalid";
            var newElement = new ResourceElement(node);
            Assert.Throws<FormatException>(() => newElement.Instance.ToPoco<Resource>().ToJson());

            Assert.Equal(Samples.GetInvalidResourceJson().Replace("\r\n", "\n"), Encoding.UTF8.GetString(_serializer.Serialize(newElement)).Replace("\r\n", "\n"));
        }

        [Fact]
        public void GivenARawResourceInXmlFormat_WhenSerialized_ThenCorrectByteArrayShouldBeProduced()
        {
            var rawResource = new RawResource(
                new FhirXmlSerializer().SerializeToString(_resource),
                FhirResourceFormat.Xml,
                isMetaSet: false);

            ResourceWrapper resourceWrapper = CreateResourceWrapper(rawResource);
            ResourceElement element = _resourceDeserializaer.DeserializeRaw(resourceWrapper.RawResource, resourceWrapper.Version, resourceWrapper.LastModified);

            byte[] actualBytes = _serializer.Serialize(element);

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

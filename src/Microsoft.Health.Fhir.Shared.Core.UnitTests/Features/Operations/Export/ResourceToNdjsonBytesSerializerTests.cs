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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Sdk;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
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
        private readonly IIgnixaJsonSerializer _ignixaSerializer;

        private readonly Observation _resource;
        private readonly byte[] _expectedBytes;

        public ResourceToNdjsonBytesSerializerTests()
        {
            _resourceDeserializaer = new ResourceDeserializer(
                (FhirResourceFormat.Json, new Func<string, string, DateTimeOffset, ResourceElement>((str, version, lastModified) => _jsonParser.Parse<Resource>(str).ToResourceElement())),
                (FhirResourceFormat.Xml, new Func<string, string, DateTimeOffset, ResourceElement>((str, version, lastModified) => _xmlParser.Parse<Resource>(str).ToResourceElement())));

            _ignixaSerializer = new IgnixaJsonSerializer();

            _serializer = new ResourceToNdjsonBytesSerializer(_ignixaSerializer);

            _resource = Samples.GetDefaultObservation().ToPoco<Observation>();
            _resource.Id = "test";

            // Expected bytes use Firely serialization format since the test deserializer
            // creates Firely-based ResourceElements without Ignixa nodes (legacy fallback path)
            string firelyJson = new FhirJsonSerializer().SerializeToString(_resource);
            string expectedString = $"{firelyJson}\n";

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
        public void GivenFirelyMode_WhenSerialized_ThenIgnixaSerializerIsNotRequired()
        {
            var modeProvider = new SdkModeProvider(new SdkConfiguration { Mode = FhirSdkMode.Firely });
            var serializer = new ResourceToNdjsonBytesSerializer(
                ignixaSerializer: null,
                modeProvider,
                new SdkFallbackGuard(modeProvider, NullLogger<SdkFallbackGuard>.Instance));

            byte[] actualBytes = serializer.Serialize(_resource.ToResourceElement());

            Assert.Equal(_expectedBytes, actualBytes);
        }

        [Fact]
        public void GivenIgnixaBackedResource_WhenSerialized_ThenIgnixaNodeIsSerializedDirectly()
        {
            var ignixaSerializer = Substitute.For<IIgnixaJsonSerializer>();
            var schemaContext = new IgnixaSchemaContext(ModelInfoProvider.Instance);
            var resourceNode = _ignixaSerializer.Parse(new FhirJsonSerializer().SerializeToString(_resource));
            var resourceElement = new IgnixaResourceElement(resourceNode, schemaContext.Schema).ToResourceElement();
            var modeProvider = new SdkModeProvider(new SdkConfiguration { Mode = FhirSdkMode.Ignixa });
            ignixaSerializer.Serialize(resourceNode, pretty: false).Returns("{\"resourceType\":\"Observation\",\"id\":\"test\"}");
            var serializer = new ResourceToNdjsonBytesSerializer(
                ignixaSerializer,
                modeProvider,
                new SdkFallbackGuard(modeProvider, NullLogger<SdkFallbackGuard>.Instance));

            serializer.Serialize(resourceElement);

            ignixaSerializer.Received().Serialize(
                Arg.Is<global::Ignixa.Serialization.SourceNodes.ResourceJsonNode>(x => ReferenceEquals(x, resourceNode)),
                pretty: false);
        }

        [Fact]
        public void GivenIgnixaModeAndFirelyBackedResource_WhenSerialized_ThenInvalidOperationExceptionIsThrown()
        {
            var modeProvider = new SdkModeProvider(new SdkConfiguration { Mode = FhirSdkMode.Ignixa });
            var serializer = new ResourceToNdjsonBytesSerializer(
                _ignixaSerializer,
                modeProvider,
                new SdkFallbackGuard(modeProvider, NullLogger<SdkFallbackGuard>.Instance));

            Assert.Throws<InvalidOperationException>(() => serializer.Serialize(_resource.ToResourceElement()));
        }

        [Fact]
        public void GivenHybridModeAndFirelyBackedResource_WhenSerialized_ThenFirelyFallbackIsGuarded()
        {
            var modeProvider = new SdkModeProvider(new SdkConfiguration { Mode = FhirSdkMode.Hybrid });
            var fallbackGuard = Substitute.For<ISdkFallbackGuard>();
            var serializer = new ResourceToNdjsonBytesSerializer(
                _ignixaSerializer,
                modeProvider,
                fallbackGuard);

            serializer.Serialize(_resource.ToResourceElement());

            fallbackGuard.Received().FirelyFallback(
                "Export NDJSON serialization",
                "ResourceElement was not backed by an Ignixa ResourceJsonNode.");
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

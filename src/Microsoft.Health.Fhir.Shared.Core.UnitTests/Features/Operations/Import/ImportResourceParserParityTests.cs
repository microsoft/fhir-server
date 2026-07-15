// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#if NET9_0_OR_GREATER

using System;
using System.Text.Json.Nodes;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Features.Compartment;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.FirelySdk.Features.Operations.Import;
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Ignixa.Features.Operations.Import;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Operations.Import
{
    /// <summary>
    /// Parity tests that assert the Firely and Ignixa <see cref="IImportResourceParser"/> implementations
    /// behave equivalently for the $import operation across all supported FHIR versions.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
    public class ImportResourceParserParityTests
    {
        private readonly IImportResourceParser _firelyParser;
        private readonly IImportResourceParser _ignixaParser;

        public ImportResourceParserParityTests()
        {
            var requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            requestContextAccessor.RequestContext.Method.Returns("PUT");
            requestContextAccessor.RequestContext.Uri.Returns(new Uri("https://unittest/Patient/123"));

            var wrapperFactory = new ResourceWrapperFactory(
                new RawResourceFactory(new FhirJsonSerializer()),
                requestContextAccessor,
                Substitute.For<ISearchIndexer>(),
                Substitute.For<IClaimsExtractor>(),
                Substitute.For<ICompartmentIndexer>(),
                Substitute.For<ISearchParameterDefinitionManager>(),
                Deserializers.ResourceDeserializer);

            _firelyParser = new FirelyImportResourceParser(new FhirJsonParser(), wrapperFactory);
            _ignixaParser = new IgnixaImportResourceParser(
                wrapperFactory,
                new IgnixaSchemaContext(new VersionSpecificModelInfoProvider()));
        }

        [Fact]
        public void GivenValidResource_WhenParsed_ThenProvidersProduceEquivalentImportMetadata()
        {
            const string json = """
                {
                  "resourceType": "Patient",
                  "id": "patient-1",
                  "meta": {
                    "versionId": "7",
                    "lastUpdated": "2026-01-02T03:04:05.123Z"
                  },
                  "active": true
                }
                """;

            ImportResource firely = _firelyParser.Parse(4, 10, json.Length, json, ImportMode.IncrementalLoad);
            ImportResource ignixa = _ignixaParser.Parse(4, 10, json.Length, json, ImportMode.IncrementalLoad);

            Assert.Equal(firely.Index, ignixa.Index);
            Assert.Equal(firely.Offset, ignixa.Offset);
            Assert.Equal(firely.Length, ignixa.Length);
            Assert.Equal(firely.KeepLastUpdated, ignixa.KeepLastUpdated);
            Assert.Equal(firely.KeepVersion, ignixa.KeepVersion);
            Assert.Equal(firely.IsDeleted, ignixa.IsDeleted);
            Assert.Equal(firely.ResourceWrapper.ResourceId, ignixa.ResourceWrapper.ResourceId);
            Assert.Equal(firely.ResourceWrapper.Version, ignixa.ResourceWrapper.Version);
            Assert.Equal(firely.ResourceWrapper.ResourceTypeName, ignixa.ResourceWrapper.ResourceTypeName);
            Assert.Equal(firely.ResourceWrapper.LastModified, ignixa.ResourceWrapper.LastModified);
            Assert.True(
                JsonNode.DeepEquals(
                    JsonNode.Parse(firely.ResourceWrapper.RawResource.Data),
                    JsonNode.Parse(ignixa.ResourceWrapper.RawResource.Data)));
        }

        [Theory]
        [InlineData("valid-id", false)]
        [InlineData("a1B2c.3d4E5f-", false)]
        [InlineData("0123456789012345678901234567890123456789012345678901234567890123", false)] // 64 chars, valid
        [InlineData(null, true)]
        [InlineData("", true)]
        [InlineData("contains/slash", true)]
        [InlineData("01234567890123456789012345678901234567890123456789012345678901234", true)]
        public void GivenResourceIdCorpus_WhenParsed_ThenProvidersAgreeOnSuccessOrBadRequestException(string id, bool shouldThrow)
        {
            string json = id is null
                ? """{"resourceType":"Patient"}"""
                : $$"""{"resourceType":"Patient","id":"{{id}}"}""";

            Exception firely = Record.Exception(() => _firelyParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad));
            Exception ignixa = Record.Exception(() => _ignixaParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad));

            Assert.Equal(shouldThrow, firely != null);
            Assert.Equal(shouldThrow, ignixa != null);

            if (shouldThrow)
            {
                Assert.IsType<BadRequestException>(firely);
                Assert.IsType<BadRequestException>(ignixa);
            }
        }

        [Fact]
        public void GivenInitialLoad_WhenParsed_ThenProvidersResetVersionAndLastUpdated()
        {
            const string json = """
                {
                  "resourceType":"Patient",
                  "id":"patient-1",
                  "meta":{"versionId":"9","lastUpdated":"2020-01-01T00:00:00Z"}
                }
                """;

            ImportResource firely = _firelyParser.Parse(0, 0, json.Length, json, ImportMode.InitialLoad);
            ImportResource ignixa = _ignixaParser.Parse(0, 0, json.Length, json, ImportMode.InitialLoad);

            Assert.False(firely.KeepVersion);
            Assert.False(ignixa.KeepVersion);
            Assert.Equal("1", firely.ResourceWrapper.Version);
            Assert.Equal("1", ignixa.ResourceWrapper.Version);
            Assert.NotEqual(default, firely.ResourceWrapper.LastModified);
            Assert.NotEqual(default, ignixa.ResourceWrapper.LastModified);

            // Both providers truncate lastUpdated to millisecond precision; sub-millisecond ticks must be zero.
            Assert.Equal(0, firely.ResourceWrapper.LastModified.Ticks % TimeSpan.TicksPerMillisecond);
            Assert.Equal(0, ignixa.ResourceWrapper.LastModified.Ticks % TimeSpan.TicksPerMillisecond);
        }

        [Fact]
        public void GivenMissingMetaAndInvalidVersion_WhenParsedOnIncrementalLoad_ThenBothProvidersResetVersion()
        {
            const string json = """
                {
                  "resourceType":"Patient",
                  "id":"patient-1"
                }
                """;

            ImportResource firely = _firelyParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad);
            ImportResource ignixa = _ignixaParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad);

            Assert.False(firely.KeepVersion);
            Assert.False(ignixa.KeepVersion);
            Assert.False(firely.KeepLastUpdated);
            Assert.False(ignixa.KeepLastUpdated);
            Assert.Equal("1", firely.ResourceWrapper.Version);
            Assert.Equal("1", ignixa.ResourceWrapper.Version);
        }

        [Fact]
        public void GivenFutureLastUpdated_WhenParsed_ThenBothProvidersReject()
        {
            const string json = """
                {
                  "resourceType":"Patient",
                  "id":"patient-1",
                  "meta":{"lastUpdated":"2999-01-01T00:00:00Z"}
                }
                """;

            Assert.Throws<NotSupportedException>(
                () => _firelyParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad));
            Assert.Throws<NotSupportedException>(
                () => _ignixaParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad));
        }

        [Theory]
        [InlineData(ImportMode.InitialLoad, true)]
        [InlineData(ImportMode.IncrementalLoad, false)]
        public void GivenTopLevelConditionalReference_WhenParsed_ThenProvidersAgree(
            ImportMode importMode,
            bool shouldThrow)
        {
            const string json = """
                {
                  "resourceType":"Observation",
                  "id":"obs-1",
                  "subject":{"reference":"Patient?identifier=system|value"},
                  "status":"final",
                  "code":{"text":"test"}
                }
                """;

            Exception firely = Record.Exception(() => _firelyParser.Parse(0, 0, json.Length, json, importMode));
            Exception ignixa = Record.Exception(() => _ignixaParser.Parse(0, 0, json.Length, json, importMode));

            Assert.Equal(shouldThrow, firely != null);
            Assert.Equal(shouldThrow, ignixa != null);

            if (shouldThrow)
            {
                Assert.IsType<NotSupportedException>(firely);
                Assert.IsType<NotSupportedException>(ignixa);
            }
        }

        [Theory]
        [InlineData(ImportMode.InitialLoad, true)]
        [InlineData(ImportMode.IncrementalLoad, false)]
        public void GivenContainedConditionalReference_WhenParsed_ThenProvidersAgree(
            ImportMode importMode,
            bool shouldThrow)
        {
            const string json = """
                {
                  "resourceType":"Patient",
                  "id":"patient-1",
                  "contained":[{
                    "resourceType":"Observation",
                    "id":"obs-1",
                    "subject":{"reference":"Patient?identifier=system|value"},
                    "status":"final",
                    "code":{"text":"test"}
                  }]
                }
                """;

            Exception firely = Record.Exception(() => _firelyParser.Parse(0, 0, json.Length, json, importMode));
            Exception ignixa = Record.Exception(() => _ignixaParser.Parse(0, 0, json.Length, json, importMode));

            Assert.Equal(shouldThrow, firely != null);
            Assert.Equal(shouldThrow, ignixa != null);

            if (shouldThrow)
            {
                Assert.IsType<NotSupportedException>(firely);
                Assert.IsType<NotSupportedException>(ignixa);
            }
        }

        [Fact]
        public void GivenBundleEntryConditionalReferenceDuringInitialLoad_WhenParsed_ThenBothProvidersReject()
        {
            const string json = """
                {
                  "resourceType":"Bundle",
                  "id":"bundle-1",
                  "type":"collection",
                  "entry":[{
                    "resource":{
                      "resourceType":"Observation",
                      "id":"obs-1",
                      "subject":{"reference":"Patient?identifier=system|value"},
                      "status":"final",
                      "code":{"text":"test"}
                    }
                  }]
                }
                """;

            Assert.Throws<NotSupportedException>(
                () => _firelyParser.Parse(0, 0, json.Length, json, ImportMode.InitialLoad));
            Assert.Throws<NotSupportedException>(
                () => _ignixaParser.Parse(0, 0, json.Length, json, ImportMode.InitialLoad));
        }

        [Fact]
        public void GivenBundleEntryConditionalReferenceDuringIncrementalLoad_WhenParsed_ThenBothProvidersAllow()
        {
            const string json = """
                {
                  "resourceType":"Bundle",
                  "id":"bundle-1",
                  "type":"collection",
                  "entry":[{
                    "resource":{
                      "resourceType":"Observation",
                      "id":"obs-1",
                      "subject":{"reference":"Patient?identifier=system|value"},
                      "status":"final",
                      "code":{"text":"test"}
                    }
                  }]
                }
                """;

            Exception firely = Record.Exception(
                () => _firelyParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad));
            Exception ignixa = Record.Exception(
                () => _ignixaParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad));

            Assert.Null(firely);
            Assert.Null(ignixa);
        }

        [Fact]
        public void GivenSoftDeletedResource_WhenParsed_ThenBothProvidersRemoveExtensionAndMarkDeleted()
        {
            string json = $$"""
                {
                  "resourceType":"Patient",
                  "id":"patient-1",
                  "meta":{
                    "extension":[{
                      "url":"{{KnownFhirPaths.AzureSoftDeletedExtensionUrl}}",
                      "valueString":"soft-deleted"
                    }]
                  }
                }
                """;

            ImportResource firely = _firelyParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad);
            ImportResource ignixa = _ignixaParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad);

            Assert.True(firely.IsDeleted);
            Assert.True(ignixa.IsDeleted);
            Assert.DoesNotContain(
                KnownFhirPaths.AzureSoftDeletedExtensionUrl,
                firely.ResourceWrapper.RawResource.Data,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                KnownFhirPaths.AzureSoftDeletedExtensionUrl,
                ignixa.ResourceWrapper.RawResource.Data,
                StringComparison.Ordinal);
            Assert.DoesNotContain("soft-deleted", firely.ResourceWrapper.RawResource.Data, StringComparison.Ordinal);
            Assert.DoesNotContain("soft-deleted", ignixa.ResourceWrapper.RawResource.Data, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("{")]
        [InlineData("""{"resourceType":"NoSuchResource","id":"x"}""")]
        public void GivenInvalidResourceJson_WhenParsed_ThenNeitherProviderReturnsSuccess(string json)
        {
            Exception firely = Record.Exception(
                () => _firelyParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad));
            Exception ignixa = Record.Exception(
                () => _ignixaParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad));

            Assert.NotNull(firely);
            Assert.NotNull(ignixa);
        }

        [Fact]
        public void GivenMalformedJson_WhenParsed_ThenIgnixaNormalizesToFormatExceptionWithInnerException()
        {
            const string json = "{ not valid json";

            Exception ignixa = Record.Exception(
                () => _ignixaParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad));

            var formatException = Assert.IsType<FormatException>(ignixa);
            Assert.NotNull(formatException.InnerException);
            Assert.IsType<System.Text.Json.JsonException>(formatException.InnerException);
        }
    }
}

#endif

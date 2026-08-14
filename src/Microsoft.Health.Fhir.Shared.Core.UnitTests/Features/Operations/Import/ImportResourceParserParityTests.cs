// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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
        public void GivenNonNumericVersionId_WhenParsedOnIncrementalLoad_ThenBothProvidersResetVersion()
        {
            const string json = """
                {
                  "resourceType":"Patient",
                  "id":"patient-1",
                  "meta":{"versionId":"not-a-number","lastUpdated":"2020-01-01T00:00:00Z"}
                }
                """;

            ImportResource firely = _firelyParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad);
            ImportResource ignixa = _ignixaParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad);

            Assert.False(firely.KeepVersion);
            Assert.False(ignixa.KeepVersion);
            Assert.Equal("1", firely.ResourceWrapper.Version);
            Assert.Equal("1", ignixa.ResourceWrapper.Version);
            Assert.Equal(firely.KeepLastUpdated, ignixa.KeepLastUpdated);
            Assert.Equal(firely.IsDeleted, ignixa.IsDeleted);
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

        // Identifier-only and display-only references are valid FHIR (no "reference" string member at all).
        // Regression coverage for a real NullReferenceException: reading the reference field through
        // ReferenceJsonNode.Reference returns null (not empty string) when "reference" is absent, so the
        // conditional-reference check must treat null the same as "no conditional reference" rather than
        // dereferencing it directly.
        [Theory]
        [InlineData(ImportMode.InitialLoad)]
        [InlineData(ImportMode.IncrementalLoad)]
        public void GivenIdentifierOnlyReference_WhenParsed_ThenBothProvidersAllow(ImportMode importMode)
        {
            const string json = """
                {
                  "resourceType":"Observation",
                  "id":"obs-1",
                  "subject":{"identifier":{"system":"http://example.org","value":"123"}},
                  "status":"final",
                  "code":{"text":"test"}
                }
                """;

            Assert.Null(Record.Exception(() => _firelyParser.Parse(0, 0, json.Length, json, importMode)));
            Assert.Null(Record.Exception(() => _ignixaParser.Parse(0, 0, json.Length, json, importMode)));
        }

        [Theory]
        [InlineData(ImportMode.InitialLoad)]
        [InlineData(ImportMode.IncrementalLoad)]
        public void GivenDisplayOnlyReference_WhenParsed_ThenBothProvidersAllow(ImportMode importMode)
        {
            const string json = """
                {
                  "resourceType":"Observation",
                  "id":"obs-1",
                  "subject":{"display":"Some Patient"},
                  "status":"final",
                  "code":{"text":"test"}
                }
                """;

            Assert.Null(Record.Exception(() => _firelyParser.Parse(0, 0, json.Length, json, importMode)));
            Assert.Null(Record.Exception(() => _ignixaParser.Parse(0, 0, json.Length, json, importMode)));
        }

        // Regression coverage: a reference field present but not a JSON object at all (schema-invalid) must
        // not be silently skipped - resource.ToElement(schema) does NOT reject this shape on its own, so the
        // conditional-reference check is the only place that catches it. Firely's FhirJsonParser (configured
        // with PermissiveParsing in FhirModule.cs) throws its own StructuralTypeException for this shape, so
        // both providers are expected to reject it, even though the exact exception type differs.
        [Fact]
        public void GivenReferenceFieldIsNotAnObject_WhenParsedOnInitialLoad_ThenBothProvidersReject()
        {
            const string json = """
                {
                  "resourceType":"Observation",
                  "id":"obs-1",
                  "subject":"Patient/123",
                  "status":"final",
                  "code":{"text":"test"}
                }
                """;

            Assert.NotNull(Record.Exception(() => _firelyParser.Parse(0, 0, json.Length, json, ImportMode.InitialLoad)));
            Assert.NotNull(Record.Exception(() => _ignixaParser.Parse(0, 0, json.Length, json, ImportMode.InitialLoad)));
        }

        // Firely's parser (PermissiveParsing) tolerates a lone object where a 0..* reference field
        // (Patient.generalPractitioner) expects an array, treating it as a single-element collection. Ignixa
        // must match that leniency rather than rejecting it, and must still detect a conditional reference
        // inside that lone object.
        [Fact]
        public void GivenCollectionReferenceFieldAsLoneObject_WhenParsed_ThenBothProvidersAllow()
        {
            const string json = """
                {
                  "resourceType":"Patient",
                  "id":"patient-1",
                  "generalPractitioner":{"reference":"Practitioner/1"}
                }
                """;

            Assert.Null(Record.Exception(() => _firelyParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad)));
            Assert.Null(Record.Exception(() => _ignixaParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad)));
        }

        [Fact]
        public void GivenCollectionReferenceFieldAsLoneObjectWithConditionalReference_WhenParsedOnInitialLoad_ThenBothProvidersReject()
        {
            const string json = """
                {
                  "resourceType":"Patient",
                  "id":"patient-1",
                  "generalPractitioner":{"reference":"Practitioner?identifier=system|value"}
                }
                """;

            Assert.Throws<NotSupportedException>(
                () => _firelyParser.Parse(0, 0, json.Length, json, ImportMode.InitialLoad));
            Assert.Throws<NotSupportedException>(
                () => _ignixaParser.Parse(0, 0, json.Length, json, ImportMode.InitialLoad));
        }

        // Ignixa's conditional-reference check is intentionally scoped to the resource's own direct,
        // schema-declared reference fields (via IReferenceMetadataProvider) - it does not recurse into
        // `contained` resources or Bundle entries. This matches TypedElementSearchIndexer, which likewise
        // never indexes into `contained`, and reflects that $import NDJSON carries individual resources
        // rather than transactional Bundles. Firely's parser still walks the whole object graph
        // (GetAllChildren<ResourceReference>), so these two scenarios are expected to diverge rather than
        // agree - unlike every other case in this suite.
        [Fact]
        public void GivenContainedConditionalReferenceDuringInitialLoad_WhenParsed_ThenFirelyRejectsButIgnixaAllows()
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

            Assert.Throws<NotSupportedException>(
                () => _firelyParser.Parse(0, 0, json.Length, json, ImportMode.InitialLoad));
            Assert.Null(Record.Exception(
                () => _ignixaParser.Parse(0, 0, json.Length, json, ImportMode.InitialLoad)));
        }

        [Fact]
        public void GivenContainedConditionalReferenceDuringIncrementalLoad_WhenParsed_ThenBothProvidersAllow()
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

            Assert.Null(Record.Exception(
                () => _firelyParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad)));
            Assert.Null(Record.Exception(
                () => _ignixaParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad)));
        }

        [Fact]
        public void GivenBundleEntryConditionalReferenceDuringInitialLoad_WhenParsed_ThenFirelyRejectsButIgnixaAllows()
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
            Assert.Null(Record.Exception(
                () => _ignixaParser.Parse(0, 0, json.Length, json, ImportMode.InitialLoad)));
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

        [Fact]
        public void GivenSoftDeleteExtensionWithNonCanonicalValue_WhenParsed_ThenBothProvidersLeaveExtensionInPlace()
        {
            string json = $$"""
                {
                  "resourceType":"Patient",
                  "id":"patient-1",
                  "meta":{
                    "extension":[{
                      "url":"{{KnownFhirPaths.AzureSoftDeletedExtensionUrl}}",
                      "valueString":"other"
                    }]
                  }
                }
                """;

            ImportResource firely = _firelyParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad);
            ImportResource ignixa = _ignixaParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad);

            Assert.False(firely.IsDeleted);
            Assert.False(ignixa.IsDeleted);
            Assert.Contains(
                KnownFhirPaths.AzureSoftDeletedExtensionUrl,
                firely.ResourceWrapper.RawResource.Data,
                StringComparison.Ordinal);
            Assert.Contains(
                KnownFhirPaths.AzureSoftDeletedExtensionUrl,
                ignixa.ResourceWrapper.RawResource.Data,
                StringComparison.Ordinal);
        }

        [Fact]
        public void GivenSoftDeleteExtensionWithMissingValue_WhenParsed_ThenBothProvidersLeaveExtensionInPlace()
        {
            string json = $$"""
                {
                  "resourceType":"Patient",
                  "id":"patient-1",
                  "meta":{
                    "extension":[{
                      "url":"{{KnownFhirPaths.AzureSoftDeletedExtensionUrl}}"
                    }]
                  }
                }
                """;

            ImportResource firely = _firelyParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad);
            ImportResource ignixa = _ignixaParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad);

            Assert.False(firely.IsDeleted);
            Assert.False(ignixa.IsDeleted);
            Assert.Contains(
                KnownFhirPaths.AzureSoftDeletedExtensionUrl,
                firely.ResourceWrapper.RawResource.Data,
                StringComparison.Ordinal);
            Assert.Contains(
                KnownFhirPaths.AzureSoftDeletedExtensionUrl,
                ignixa.ResourceWrapper.RawResource.Data,
                StringComparison.Ordinal);
        }

        [Fact]
        public void GivenSoftDeleteExtensionUrlCasingDiffers_WhenParsed_ThenBothProvidersLeaveExtensionInPlace()
        {
            string differentCaseUrl = KnownFhirPaths.AzureSoftDeletedExtensionUrl.ToUpperInvariant();
            string json = $$"""
                {
                  "resourceType":"Patient",
                  "id":"patient-1",
                  "meta":{
                    "extension":[{
                      "url":"{{differentCaseUrl}}",
                      "valueString":"soft-deleted"
                    }]
                  }
                }
                """;

            ImportResource firely = _firelyParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad);
            ImportResource ignixa = _ignixaParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad);

            Assert.False(firely.IsDeleted);
            Assert.False(ignixa.IsDeleted);
            Assert.Contains(differentCaseUrl, firely.ResourceWrapper.RawResource.Data, StringComparison.Ordinal);
            Assert.Contains(differentCaseUrl, ignixa.ResourceWrapper.RawResource.Data, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenMultipleSoftDeleteExtensionsWhereOneIsCanonical_WhenParsed_ThenBothProvidersRemoveAllMatchingUrlExtensions()
        {
            string json = $$"""
                {
                  "resourceType":"Patient",
                  "id":"patient-1",
                  "meta":{
                    "extension":[
                      {
                        "url":"{{KnownFhirPaths.AzureSoftDeletedExtensionUrl}}",
                        "valueString":"other"
                      },
                      {
                        "url":"{{KnownFhirPaths.AzureSoftDeletedExtensionUrl}}",
                        "valueString":"soft-deleted"
                      }
                    ]
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
        }

        [Fact]
        public void GivenSoftDeleteExtensionUsesValueCode_WhenParsed_ThenBothProvidersRemoveExtensionAndMarkDeleted()
        {
            // The soft-deleted extension's value is polymorphic (value[x]). Firely's FHIRPath predicate compares
            // whichever value[x] element is present against the string literal "soft-deleted", and FHIRPath string
            // equality succeeds for any FHIR primitive that maps to the FHIRPath System.String type - which includes
            // "code", not just "string". valueCode is the shape reported by an actual soft-delete producer.
            string json = $$"""
                {
                  "resourceType":"Patient",
                  "id":"patient-1",
                  "meta":{
                    "extension":[{
                      "url":"{{KnownFhirPaths.AzureSoftDeletedExtensionUrl}}",
                      "valueCode":"soft-deleted"
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
        }

        [Fact]
        public void GivenMultipleSoftDeleteExtensionsWhereCanonicalUsesValueCode_WhenParsed_ThenBothProvidersRemoveAllMatchingUrlExtensions()
        {
            string json = $$"""
                {
                  "resourceType":"Patient",
                  "id":"patient-1",
                  "meta":{
                    "extension":[
                      {
                        "url":"{{KnownFhirPaths.AzureSoftDeletedExtensionUrl}}",
                        "valueString":"other"
                      },
                      {
                        "url":"{{KnownFhirPaths.AzureSoftDeletedExtensionUrl}}",
                        "valueCode":"soft-deleted"
                      }
                    ]
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

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text.Json.Nodes;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Compartment;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Search;
using Microsoft.Health.Fhir.FirelySdk.Features.Operations.Import;
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Ignixa.Features.Operations.Import;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

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
        private static readonly DateTimeOffset PinnedNow = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

#pragma warning disable CS0618 // Type or member is obsolete
        private static readonly FhirJsonParser ProductionFirelyJsonParser = new(new ParserSettings { PermissiveParsing = true, TruncateDateTimeToDate = true });
#pragma warning restore CS0618 // Type or member is obsolete

        private readonly IgnixaSchemaContext _schemaContext = new(new VersionSpecificModelInfoProvider());
        private readonly IImportResourceParser _firelyParser;
        private readonly IImportResourceParser _ignixaParser;
        private readonly ResourceWrapperFactory _wrapperFactory;

        public ImportResourceParserParityTests()
        {
            _wrapperFactory = CreateWrapperFactory(Substitute.For<ISearchIndexer>());
            _firelyParser = new FirelyImportResourceParser(new FhirJsonParser(), _wrapperFactory, ResourceIdPolicy.Standard);
            _ignixaParser = CreateIgnixaParser(_wrapperFactory);
        }

        public static TheoryData<string, ImportMode> AcceptedParityCorpus => new()
        {
            {
                """{"resourceType":"Patient","id":"patient-1","meta":{"versionId":"7","lastUpdated":"2026-01-02T03:04:05.123Z"},"active":true}""",
                ImportMode.IncrementalLoad
            },
            {
                """{"resourceType":"Patient","id":"patient-1","meta":{"versionId":"9","lastUpdated":"2020-01-01T00:00:00Z"}}""",
                ImportMode.InitialLoad
            },
            {
                """{"resourceType":"Patient","id":"patient-1","meta":{"versionId":"not-a-number","lastUpdated":"2020-01-01T00:00:00Z"}}""",
                ImportMode.IncrementalLoad
            },
            {
                """{"resourceType":"Observation","id":"obs-1","subject":{"identifier":{"system":"http://example.org","value":"123"}},"status":"final","code":{"text":"test"}}""",
                ImportMode.InitialLoad
            },
            {
                """{"resourceType":"Observation","id":"obs-1","subject":{"display":"Some Patient"},"status":"final","code":{"text":"test"}}""",
                ImportMode.IncrementalLoad
            },
            {
                """{"resourceType":"Patient","id":"patient-1","generalPractitioner":{"reference":"Practitioner/1"}}""",
                ImportMode.IncrementalLoad
            },
            {
                """{"resourceType":"Patient","id":"patient-1","contained":[{"resourceType":"Observation","id":"obs-1","subject":{"reference":"Patient?identifier=system|value"},"status":"final","code":{"text":"test"}}]}""",
                ImportMode.IncrementalLoad
            },
            {
                """{"resourceType":"Bundle","id":"bundle-1","type":"collection","entry":[{"resource":{"resourceType":"Observation","id":"obs-1","subject":{"reference":"Patient?identifier=system|value"},"status":"final","code":{"text":"test"}}}]}""",
                ImportMode.IncrementalLoad
            },
            {
                $$$"""{"resourceType":"Patient","id":"patient-1","meta":{"extension":[{"url":"{{{KnownFhirPaths.AzureSoftDeletedExtensionUrl}}}","valueString":"soft-deleted"}]}}""",
                ImportMode.IncrementalLoad
            },
            {
                """
                {
                  "resourceType":"Patient",
                  "id":"patient-1",
                  "meta":{"lastUpdated":"2026-01-02T03:04:05.123Z"},
                  "extension":[{"url":"http://example.org/date","valueDate":"2001-02-03T04:05:06Z"}],
                  "birthDate":"2001-02-03T04:05:06+10:00",
                  "_birthDate":{"extension":[{"url":"http://example.org/note","valueString":"shadow"}]},
                  "deceasedDateTime":"2020-05-06T07:08:09Z",
                  "contained":[
                    {"resourceType":"Patient","id":"contained-1","birthDate":"1999-12-31T23:59:59-05:00"},
                    {"resourceType":"Patient","id":"contained-2","birthDate":"2001-02"}
                  ]
                }
                """,
                ImportMode.IncrementalLoad
            },
        };

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

        [Theory]
        [InlineData(false, 64, true)]
        [InlineData(false, 65, false)]
        [InlineData(false, 128, false)]
        [InlineData(true, 64, true)]
        [InlineData(true, 65, true)]
        [InlineData(true, 128, true)]
        [InlineData(true, 129, false)]
        public void GivenTheLongResourceIdsFlag_WhenParsed_ThenBothProvidersApplyTheSelectedLimit(bool useLongResourceIds, int length, bool valid)
        {
            // Arrange
            var policy = ResourceIdPolicy.From(useLongResourceIds);
            var firelyParser = new FirelyImportResourceParser(new FhirJsonParser(), _wrapperFactory, policy);
            var ignixaParser = new IgnixaImportResourceParser(_wrapperFactory, new IgnixaSchemaContext(new VersionSpecificModelInfoProvider()), policy);
            string id = new string('a', length);
            string json = $$"""{"resourceType":"Patient","id":"{{id}}"}""";

            // Act
            if (valid)
            {
                ImportResource firely = firelyParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad);
                ImportResource ignixa = ignixaParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad);

                // Assert
                Assert.Equal(id, firely.ResourceWrapper.ResourceId);
                Assert.Equal(id, ignixa.ResourceWrapper.ResourceId);
            }
            else
            {
                // Assert
                Assert.Throws<BadRequestException>(() => firelyParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad));
                Assert.Throws<BadRequestException>(() => ignixaParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad));
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

        [Fact]
        public void GivenCollectionReferenceArrayWithConditionalReference_WhenParsedOnInitialLoad_ThenBothProvidersReject()
        {
            const string json = """
                {
                  "resourceType":"Patient",
                  "id":"patient-1",
                  "generalPractitioner":[
                    {"reference":"Practitioner/1"},
                    {"reference":"Practitioner?identifier=system|value"}
                  ]
                }
                """;

            Assert.Throws<NotSupportedException>(
                () => _firelyParser.Parse(0, 0, json.Length, json, ImportMode.InitialLoad));
            Assert.Throws<NotSupportedException>(
                () => _ignixaParser.Parse(0, 0, json.Length, json, ImportMode.InitialLoad));
        }

        // Choice-type fields that the schema types as Reference ("medication[x]", R5 "Observation.value[x]")
        // serialize with the concrete type suffix (medicationReference / valueReference), so the conditional
        // reference check must map the "[x]" schema path to that JSON property name.
        [Fact]
        public void GivenChoiceTypeReferenceWithConditionalReference_WhenParsedOnInitialLoad_ThenBothProvidersReject()
        {
            string json = ModelInfoProvider.Version == FhirSpecification.R5
                ? """
                  {
                    "resourceType":"Observation",
                    "id":"obs-1",
                    "status":"final",
                    "code":{"text":"test"},
                    "valueReference":{"reference":"MolecularDefinition?identifier=system|value"}
                  }
                  """
                : """
                  {
                    "resourceType":"MedicationRequest",
                    "id":"mr-1",
                    "status":"active",
                    "intent":"order",
                    "subject":{"reference":"Patient/1"},
                    "medicationReference":{"reference":"Medication?code=system|value"}
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

        [Fact]
        public void GivenIgnixaParser_WhenParsing_ThenResourceElementCarriesNativeNode()
        {
            const string json = """{"resourceType":"Patient","id":"patient-1","active":true}""";

            ResourceElement element = ParseCapturingElement(CreateIgnixaParser, _wrapperFactory, json, ImportMode.IncrementalLoad).Element;

            ResourceJsonNode carrier = Assert.IsAssignableFrom<ResourceJsonNode>(element.ResourceInstance);
            Assert.Equal("patient-1", carrier.Id);
        }

        [Theory]
        [MemberData(nameof(AcceptedParityCorpus))]
        public void GivenNativeCarrier_WhenToPoco_ThenReturnsEquivalentPoco(string json, ImportMode importMode)
        {
            using (Mock.Property(() => ClockResolver.TimeProvider, new FakeTimeProvider(PinnedNow)))
            {
                ResourceElement firely = ParseCapturingElement(CreateProductionFirelyParser, _wrapperFactory, json, importMode).Element;
                ResourceElement ignixa = ParseCapturingElement(CreateIgnixaParser, _wrapperFactory, json, importMode).Element;

                Assert.IsAssignableFrom<ResourceJsonNode>(ignixa.ResourceInstance);
                Resource expected = firely.ToPoco<Resource>();
                Resource actual = ignixa.ToPoco<Resource>();
                var serializer = new FhirJsonSerializer();
                Assert.True(
                    actual.IsExactly(expected),
                    $"Expected {serializer.SerializeToString(expected)} but found {serializer.SerializeToString(actual)}.");
            }
        }

        [Fact]
        public async Task GivenDateTypedElementWithDateTime_WhenIgnixaParses_ThenTruncatedToDateLikeFirely()
        {
            const string json = """{"resourceType":"Patient","id":"patient-1","birthDate":"2001-02-03T04:05:06Z"}""";
            var searchIndexer = new TypedElementSearchIndexer(
                new SupportedSearchParameterDefinitionManager(await new SearchParameterFixtureData().GetSearchDefinitionManagerAsync()),
                await SearchParameterFixtureData.GetFhirTypedElementToSearchValueConverterManagerAsync(),
                Substitute.For<IReferenceToElementResolver>(),
                ModelInfoProvider.Instance,
                new FirelyFhirPathProvider(),
                NullLogger<TypedElementSearchIndexer>.Instance);
            ResourceWrapperFactory wrapperFactory = CreateWrapperFactory(searchIndexer);

            (ImportResource firely, _) = ParseCapturingElement(CreateProductionFirelyParser, wrapperFactory, json, ImportMode.IncrementalLoad);
            (ImportResource ignixa, ResourceElement ignixaElement) = ParseCapturingElement(CreateIgnixaParser, wrapperFactory, json, ImportMode.IncrementalLoad);

            ResourceJsonNode carrier = Assert.IsAssignableFrom<ResourceJsonNode>(ignixaElement.ResourceInstance);
            JsonObject carriedJson = Assert.IsType<JsonObject>(carrier.ToSourceNavigator().Meta<JsonNode>());
            Assert.Equal("2001-02-03", carriedJson["birthDate"].GetValue<string>());
            Assert.Equal("2001-02-03", GetBirthDateIndexValue(ignixa));
            Assert.Equal(GetBirthDateIndexValue(firely), GetBirthDateIndexValue(ignixa));
        }

        private static ResourceWrapperFactory CreateWrapperFactory(ISearchIndexer searchIndexer)
        {
            var requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            requestContextAccessor.RequestContext.Method.Returns("PUT");
            requestContextAccessor.RequestContext.Uri.Returns(new Uri("https://unittest/Patient/123"));

            return new ResourceWrapperFactory(
                new RawResourceFactory(new FhirJsonSerializer()),
                requestContextAccessor,
                searchIndexer,
                Substitute.For<IClaimsExtractor>(),
                Substitute.For<ICompartmentIndexer>(),
                Substitute.For<ISearchParameterDefinitionManager>(),
                Deserializers.ResourceDeserializer);
        }

        private static IImportResourceParser CreateProductionFirelyParser(IResourceWrapperFactory wrapperFactory)
            => new FirelyImportResourceParser(ProductionFirelyJsonParser, wrapperFactory, ResourceIdPolicy.Standard);

        private static (ImportResource Resource, ResourceElement Element) ParseCapturingElement(
            Func<IResourceWrapperFactory, IImportResourceParser> createParser,
            IResourceWrapperFactory wrapperFactory,
            string json,
            ImportMode importMode)
        {
            ResourceElement element = null;
            var recordingFactory = Substitute.For<IResourceWrapperFactory>();
            recordingFactory
                .Create(Arg.Any<ResourceElement>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<bool>())
                .Returns(call =>
                {
                    element = call.ArgAt<ResourceElement>(0);
                    return wrapperFactory.Create(element, call.ArgAt<bool>(1), call.ArgAt<bool>(2), call.ArgAt<bool>(3));
                });

            ImportResource resource = createParser(recordingFactory).Parse(0, 0, json.Length, json, importMode);
            return (resource, element);
        }

        private static string GetBirthDateIndexValue(ImportResource resource)
            => Assert.Single(resource.ResourceWrapper.SearchIndices, entry => entry.SearchParameter.Code == "birthdate").Value.ToString();

        private IImportResourceParser CreateIgnixaParser(IResourceWrapperFactory wrapperFactory)
            => new IgnixaImportResourceParser(wrapperFactory, _schemaContext, ResourceIdPolicy.Standard);
    }
}

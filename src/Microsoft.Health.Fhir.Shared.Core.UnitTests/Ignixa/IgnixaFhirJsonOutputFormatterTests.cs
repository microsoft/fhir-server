// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Sdk;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Ignixa;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Serialization)]
public class IgnixaFhirJsonOutputFormatterTests
{
    private static readonly FhirJsonParser Parser = new FhirJsonParser();
    private readonly IgnixaFhirJsonOutputFormatter _formatter;
    private readonly IIgnixaJsonSerializer _ignixaSerializer;

    public IgnixaFhirJsonOutputFormatterTests()
    {
        _ignixaSerializer = new IgnixaJsonSerializer();
        var firelySerializer = new FhirJsonSerializer();
        _formatter = CreateFormatter(FhirSdkMode.Hybrid);
    }

    // ------------------------------------------------------------------
    // CanWriteType
    // ------------------------------------------------------------------

    [Fact]
    public void GivenResourceType_WhenCheckingCanWrite_ThenTrueIsReturned()
    {
        Assert.True(CanWrite(typeof(Resource)));
    }

    [Fact]
    public void GivenObservationType_WhenCheckingCanWrite_ThenTrueIsReturned()
    {
        Assert.True(CanWrite(typeof(Observation)));
    }

    [Fact]
    public void GivenRawResourceElementType_WhenCheckingCanWrite_ThenTrueIsReturned()
    {
        Assert.True(CanWrite(typeof(RawResourceElement)));
    }

    [Fact]
    public void GivenJObjectType_WhenCheckingCanWrite_ThenFalseIsReturned()
    {
        Assert.False(CanWrite(typeof(JObject)));
    }

    [Fact]
    public void GivenStringType_WhenCheckingCanWrite_ThenFalseIsReturned()
    {
        Assert.False(CanWrite(typeof(string)));
    }

    [Fact]
    public void GivenResourceJsonNodeType_WhenCheckingCanWrite_ThenTrueIsReturned()
    {
        Assert.True(CanWrite(typeof(global::Ignixa.Serialization.SourceNodes.ResourceJsonNode)));
    }

    [Fact]
    public void GivenIgnixaResourceElementType_WhenCheckingCanWrite_ThenTrueIsReturned()
    {
        Assert.True(CanWrite(typeof(IgnixaResourceElement)));
    }

    // ------------------------------------------------------------------
    // WriteResponseBody — ResourceJsonNode (native Ignixa type)
    // ------------------------------------------------------------------

    [Fact]
    public async Task GivenAResourceJsonNode_WhenWritten_ThenValidJsonIsProduced()
    {
        // Arrange
        var patientJson = Samples.GetJson("Patient");
        var node = _ignixaSerializer.Parse(patientJson);

        // Act
        var json = await WriteObject(node, typeof(global::Ignixa.Serialization.SourceNodes.ResourceJsonNode));

        // Assert
        Assert.False(string.IsNullOrEmpty(json));
        var parsed = Parser.Parse<Patient>(json);
        Assert.NotNull(parsed.Id);
    }

    // ------------------------------------------------------------------
    // WriteResponseBody — IgnixaResourceElement
    // ------------------------------------------------------------------

    [Fact]
    public async Task GivenAnIgnixaResourceElement_WhenWritten_ThenValidJsonIsProduced()
    {
        // Arrange
        var patientJson = Samples.GetJson("Patient");
        var node = _ignixaSerializer.Parse(patientJson);
        var schemaContext = new IgnixaSchemaContext(ModelInfoProvider.Instance);
        var element = new IgnixaResourceElement(node, schemaContext.Schema);

        // Act
        var json = await WriteObject(element, typeof(IgnixaResourceElement));

        // Assert
        Assert.False(string.IsNullOrEmpty(json));
        var parsed = Parser.Parse<Patient>(json);
        Assert.NotNull(parsed.Id);
    }

    [Theory]
    [InlineData(typeof(IgnixaResourceElement))]
    [InlineData(typeof(global::Ignixa.Serialization.SourceNodes.ResourceJsonNode))]
    public async Task GivenIgnixaResource_WhenWrittenWithElementsParameter_ThenOnlyRequestedElementsAreWritten(Type objectType)
    {
        var patient = new Patient
        {
            Id = "elements-test",
            Active = true,
            Name = { new HumanName { Family = "Smith", Given = new[] { "John" } } },
        };
        var node = _ignixaSerializer.Parse(patient.ToJson());
        var schemaContext = new IgnixaSchemaContext(ModelInfoProvider.Instance);
        object resource = objectType == typeof(IgnixaResourceElement)
            ? new IgnixaResourceElement(node, schemaContext.Schema)
            : node;

        var json = await WriteObject(resource, objectType, "?_elements=active");

        var parsed = Parser.Parse<Patient>(json);
        Assert.True(parsed.Active);
        Assert.Empty(parsed.Name);
    }

    [Fact]
    public async Task GivenIgnixaModeAndElementsProjection_WhenWritingResourceJsonNode_ThenProjectionIsAppliedWithoutFirelyFallback()
    {
        // Arrange
        var formatter = CreateFormatter(FhirSdkMode.Ignixa);
        var patient = new Patient
        {
            Id = "projection-test",
            Active = true,
            Name = { new HumanName { Family = "Hidden" } },
        };
        var node = _ignixaSerializer.Parse(patient.ToJson());

        // Act
        var json = await WriteObject(formatter, node, typeof(global::Ignixa.Serialization.SourceNodes.ResourceJsonNode), "?_elements=active");

        // Assert
        var parsed = Parser.Parse<Patient>(json);
        Assert.Equal("projection-test", parsed.Id);
        Assert.True(parsed.Active);
        Assert.Empty(parsed.Name);
    }

    [Fact]
    public async Task GivenIgnixaModeAndSummaryDataProjection_WhenWritingResourceJsonNode_ThenProjectionIsAppliedWithoutFirelyFallback()
    {
        // Arrange
        var formatter = CreateFormatter(FhirSdkMode.Ignixa);
        var patient = new Patient
        {
            Id = "summary-data-test",
            Active = true,
            Text = new Narrative
            {
                Status = Narrative.NarrativeStatus.Generated,
                Div = "<div xmlns=\"http://www.w3.org/1999/xhtml\">Hidden narrative</div>",
            },
            Name = { new HumanName { Family = "Visible", Given = new[] { "Data" } } },
            Contact =
            {
                new Patient.ContactComponent
                {
                    Name = new HumanName { Family = "Contact" },
                },
            },
        };
        var node = _ignixaSerializer.Parse(patient.ToJson());

        // Act
        var json = await WriteObject(formatter, node, typeof(global::Ignixa.Serialization.SourceNodes.ResourceJsonNode), "?_summary=data");

        // Assert
        var parsed = Parser.Parse<Patient>(json);
        Assert.Equal("summary-data-test", parsed.Id);
        Assert.True(parsed.Active);
        Assert.Single(parsed.Name);
        Assert.Equal("Visible", parsed.Name[0].Family);
        Assert.NotEmpty(parsed.Contact);
        Assert.Null(parsed.Text);
    }

    [Fact]
    public async Task GivenIgnixaModeAndSummaryTextProjection_WhenWritingResourceJsonNode_ThenOnlyTextAndMandatoryFieldsArePreserved()
    {
        // Arrange
        var formatter = CreateFormatter(FhirSdkMode.Ignixa);
        var patient = new Patient
        {
            Id = "summary-text-test",
            Active = true,
            Text = new Narrative
            {
                Status = Narrative.NarrativeStatus.Generated,
                Div = "<div xmlns=\"http://www.w3.org/1999/xhtml\">Visible narrative</div>",
            },
            Name = { new HumanName { Family = "Hidden" } },
        };
        var node = _ignixaSerializer.Parse(patient.ToJson());

        // Act
        var json = await WriteObject(formatter, node, typeof(global::Ignixa.Serialization.SourceNodes.ResourceJsonNode), "?_summary=text");

        // Assert
        var parsed = Parser.Parse<Patient>(json);
        Assert.Equal("summary-text-test", parsed.Id);
        Assert.NotNull(parsed.Text);
        Assert.Null(parsed.ActiveElement);
        Assert.Empty(parsed.Name);
    }

    [Fact]
    public async Task GivenIgnixaModeAndSummaryTextProjection_WhenResourceHasMandatoryElements_ThenMandatoryElementsArePreserved()
    {
        // Arrange
        var formatter = CreateFormatter(FhirSdkMode.Ignixa);
        var observation = new Observation
        {
            Id = "summary-text-mandatory-test",
            Status = ObservationStatus.Final,
            Code = new CodeableConcept("http://loinc.org", "8310-5"),
            Text = new Narrative
            {
                Status = Narrative.NarrativeStatus.Generated,
                Div = "<div xmlns=\"http://www.w3.org/1999/xhtml\">Visible narrative</div>",
            },
            Value = new Quantity(98.6m, "F"),
        };
        var node = _ignixaSerializer.Parse(observation.ToJson());

        // Act
        var json = await WriteObject(formatter, node, typeof(global::Ignixa.Serialization.SourceNodes.ResourceJsonNode), "?_summary=text");

        // Assert
        var parsed = Parser.Parse<Observation>(json);
        Assert.Equal("summary-text-mandatory-test", parsed.Id);
        Assert.NotNull(parsed.Text);
        Assert.Equal(ObservationStatus.Final, parsed.Status);
        Assert.NotNull(parsed.Code);
        Assert.Null(parsed.Value);
    }

    [Fact]
    public async Task GivenIgnixaModeAndSummaryTrueProjection_WhenWritingResourceJsonNode_ThenSummaryElementsArePreserved()
    {
        // Arrange
        var formatter = CreateFormatter(FhirSdkMode.Ignixa);
        var patient = new Patient
        {
            Id = "summary-true-test",
            BirthDate = "1980-01-01",
            Text = new Narrative
            {
                Status = Narrative.NarrativeStatus.Generated,
                Div = "<div xmlns=\"http://www.w3.org/1999/xhtml\">Hidden narrative</div>",
            },
            Contact =
            {
                new Patient.ContactComponent
                {
                    Name = new HumanName { Family = "Hidden" },
                },
            },
        };
        var node = _ignixaSerializer.Parse(patient.ToJson());

        // Act
        var json = await WriteObject(formatter, node, typeof(global::Ignixa.Serialization.SourceNodes.ResourceJsonNode), "?_summary=true");

        // Assert
        var parsed = Parser.Parse<Patient>(json);
        Assert.Equal("summary-true-test", parsed.Id);
        Assert.Equal("1980-01-01", parsed.BirthDate);
        Assert.Empty(parsed.Contact);
        Assert.Null(parsed.Text);
    }

    [Theory]
    [InlineData("?_elements=active")]
    [InlineData("?_summary=true")]
    public async Task GivenIgnixaModeAndProjection_WhenWritingResourceJsonNode_ThenSubsettedTagIsAdded(string query)
    {
        // Arrange
        var formatter = CreateFormatter(FhirSdkMode.Ignixa);
        var patient = new Patient
        {
            Id = "subsetted-test",
            Active = true,
            BirthDate = "1980-01-01",
            Name = { new HumanName { Family = "Hidden" } },
        };
        var node = _ignixaSerializer.Parse(patient.ToJson());

        // Act
        var json = await WriteObject(formatter, node, typeof(global::Ignixa.Serialization.SourceNodes.ResourceJsonNode), query);

        // Assert
        var parsed = Parser.Parse<Patient>(json);
        Assert.Equal("subsetted-test", parsed.Id);
        AssertContainsSubsettedTag(parsed.Meta);
    }

    [Theory]
    [InlineData("?_count=0")]
    [InlineData("?_summary=count")]
    public async Task GivenIgnixaModeAndCountProjection_WhenWritingBundleResourceJsonNode_ThenCountShapeIsPreserved(string query)
    {
        // Arrange
        var formatter = CreateFormatter(FhirSdkMode.Ignixa);
        var bundle = new Bundle
        {
            Id = "count-bundle-test",
            Type = Bundle.BundleType.Searchset,
            Total = 42,
            Entry =
            {
                new Bundle.EntryComponent
                {
                    Resource = new Patient { Id = "hidden-entry" },
                },
            },
        };
        var node = _ignixaSerializer.Parse(bundle.ToJson());

        // Act
        var json = await WriteObject(formatter, node, typeof(global::Ignixa.Serialization.SourceNodes.ResourceJsonNode), query);

        // Assert
        var parsed = Parser.Parse<Bundle>(json);
        Assert.Equal("count-bundle-test", parsed.Id);
        Assert.Equal(Bundle.BundleType.Searchset, parsed.Type);
        Assert.Equal(42, parsed.Total);
        Assert.Empty(parsed.Entry);

        var jsonObject = JObject.Parse(json);
        Assert.True(jsonObject.ContainsKey("type"));
        Assert.True(jsonObject.ContainsKey("total"));
        Assert.False(jsonObject.ContainsKey("entry"));
    }

    [Fact]
    public async Task GivenIgnixaModeAndElementsProjection_WhenWritingBundleResourceJsonNode_ThenEntryResourcesAreProjectedAndTagged()
    {
        // Arrange
        var formatter = CreateFormatter(FhirSdkMode.Ignixa);
        var bundle = new Bundle
        {
            Id = "bundle-elements-test",
            Type = Bundle.BundleType.Searchset,
            Total = 1,
            Entry =
            {
                new Bundle.EntryComponent
                {
                    Resource = new Patient
                    {
                        Id = "bundle-patient",
                        Active = true,
                        Name = { new HumanName { Family = "Hidden" } },
                    },
                },
            },
        };
        var node = _ignixaSerializer.Parse(bundle.ToJson());

        // Act
        var json = await WriteObject(formatter, node, typeof(global::Ignixa.Serialization.SourceNodes.ResourceJsonNode), "?_elements=active");

        // Assert
        var parsed = Parser.Parse<Bundle>(json);
        Assert.Equal("bundle-elements-test", parsed.Id);
        Assert.Equal(Bundle.BundleType.Searchset, parsed.Type);
        Assert.Equal(1, parsed.Total);
        var entry = Assert.Single(parsed.Entry);
        var patient = Assert.IsType<Patient>(entry.Resource);
        Assert.Equal("bundle-patient", patient.Id);
        Assert.True(patient.Active);
        Assert.Empty(patient.Name);
        AssertContainsSubsettedTag(patient.Meta);

        var jsonObject = JObject.Parse(json);
        Assert.True(jsonObject.ContainsKey("entry"));
        Assert.Contains(
            jsonObject["entry"]![0]!["resource"]!["meta"]!["tag"]!.Children(),
            tag => string.Equals((string)tag!["code"]!, "SUBSETTED", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GivenIgnixaModeAndElementsProjection_WhenBundleEntryResourceElementIsRequested_ThenNestedResourceDiscriminatorIsPreserved()
    {
        // Arrange
        var formatter = CreateFormatter(FhirSdkMode.Ignixa);
        var bundle = new Bundle
        {
            Id = "bundle-entry-resource-elements-test",
            Type = Bundle.BundleType.Searchset,
            Total = 1,
            Entry =
            {
                new Bundle.EntryComponent
                {
                    Resource = new Patient
                    {
                        Id = "nested-resource-patient",
                        Active = true,
                        Name = { new HumanName { Family = "Hidden" } },
                    },
                },
            },
        };
        var node = _ignixaSerializer.Parse(bundle.ToJson());

        // Act
        var json = await WriteObject(formatter, node, typeof(global::Ignixa.Serialization.SourceNodes.ResourceJsonNode), "?_elements=entry.resource.active");

        // Assert
        var parsed = Parser.Parse<Bundle>(json);
        Assert.Equal("bundle-entry-resource-elements-test", parsed.Id);
        Assert.Equal(Bundle.BundleType.Searchset, parsed.Type);
        Assert.Equal(1, parsed.Total);
        var entry = Assert.Single(parsed.Entry);
        var patient = Assert.IsType<Patient>(entry.Resource);
        Assert.Equal("nested-resource-patient", patient.Id);
        Assert.True(patient.Active);
        Assert.Empty(patient.Name);
        AssertContainsSubsettedTag(patient.Meta);

        var jsonObject = JObject.Parse(json);
        Assert.Equal("Patient", (string)jsonObject["entry"]![0]!["resource"]!["resourceType"]!);
    }

    [Fact]
    public async Task GivenIgnixaModeAndElementsProjection_WhenBundleEntryResourceIsRequested_ThenUnrequestedEntrySiblingsAreOmitted()
    {
        // Arrange
        var formatter = CreateFormatter(FhirSdkMode.Ignixa);
        var bundle = CreateBundleWithEntryMetadata();
        var node = _ignixaSerializer.Parse(bundle.ToJson());

        // Act
        var json = await WriteObject(formatter, node, typeof(global::Ignixa.Serialization.SourceNodes.ResourceJsonNode), "?_elements=entry.resource.active");

        // Assert
        var parsed = Parser.Parse<Bundle>(json);
        var entry = Assert.Single(parsed.Entry);
        var patient = Assert.IsType<Patient>(entry.Resource);
        Assert.Equal("entry-metadata-patient", patient.Id);
        Assert.True(patient.Active);
        Assert.Empty(patient.Name);
        Assert.Null(entry.FullUrl);
        Assert.Null(entry.Search);
        Assert.Null(entry.Request);
        Assert.Null(entry.Response);

        var jsonObject = JObject.Parse(json);
        var entryObject = (JObject)jsonObject["entry"]![0]!;
        Assert.True(entryObject.ContainsKey("resource"));
        Assert.False(entryObject.ContainsKey("fullUrl"));
        Assert.False(entryObject.ContainsKey("search"));
        Assert.False(entryObject.ContainsKey("request"));
        Assert.False(entryObject.ContainsKey("response"));
    }

    [Fact]
    public async Task GivenIgnixaModeAndElementsProjection_WhenBundleEntryMetadataIsRequested_ThenOnlyRequestedEntryMetadataIsPreserved()
    {
        // Arrange
        var formatter = CreateFormatter(FhirSdkMode.Ignixa);
        var bundle = CreateBundleWithEntryMetadata();
        var node = _ignixaSerializer.Parse(bundle.ToJson());

        // Act
        var json = await WriteObject(formatter, node, typeof(global::Ignixa.Serialization.SourceNodes.ResourceJsonNode), "?_elements=entry.resource.active,entry.fullUrl,entry.search.mode");

        // Assert
        var parsed = Parser.Parse<Bundle>(json);
        var entry = Assert.Single(parsed.Entry);
        var patient = Assert.IsType<Patient>(entry.Resource);
        Assert.True(patient.Active);
        Assert.Equal("https://example.org/fhir/Patient/entry-metadata-patient", entry.FullUrl);
        Assert.NotNull(entry.Search);
        Assert.Equal(Bundle.SearchEntryMode.Match, entry.Search.Mode);
        Assert.Null(entry.Search.ScoreElement);
        Assert.Null(entry.Request);
        Assert.Null(entry.Response);

        var jsonObject = JObject.Parse(json);
        var entryObject = (JObject)jsonObject["entry"]![0]!;
        Assert.True(entryObject.ContainsKey("resource"));
        Assert.True(entryObject.ContainsKey("fullUrl"));
        Assert.True(entryObject.ContainsKey("search"));
        Assert.False(((JObject)entryObject["search"]!).ContainsKey("score"));
        Assert.False(entryObject.ContainsKey("request"));
        Assert.False(entryObject.ContainsKey("response"));
    }

    [Fact]
    public async Task GivenIgnixaModeAndElementsProjection_WhenBundleShellFieldIsRequested_ThenEntryIsOmitted()
    {
        // Arrange
        var formatter = CreateFormatter(FhirSdkMode.Ignixa);
        var bundle = CreateBundleWithEntryMetadata();
        var node = _ignixaSerializer.Parse(bundle.ToJson());

        // Act
        var json = await WriteObject(formatter, node, typeof(global::Ignixa.Serialization.SourceNodes.ResourceJsonNode), "?_elements=link");

        // Assert
        var parsed = Parser.Parse<Bundle>(json);
        Assert.Equal(Bundle.BundleType.Searchset, parsed.Type);
        var link = Assert.Single(parsed.Link);
        Assert.Equal("self", link.Relation);
        Assert.Equal("https://example.org/fhir/Patient?_id=entry-metadata-patient", link.Url);
        Assert.Empty(parsed.Entry);

        var jsonObject = JObject.Parse(json);
        Assert.True(jsonObject.ContainsKey("type"));
        Assert.True(jsonObject.ContainsKey("link"));
        Assert.False(jsonObject.ContainsKey("entry"));
        Assert.False(jsonObject.ContainsKey("total"));
        Assert.False(jsonObject.ContainsKey("timestamp"));
    }

    [Fact]
    public async Task GivenIgnixaModeAndSummaryTrueProjection_WhenWritingBundleResourceJsonNode_ThenEntryResourcesAreSummarizedAndTagged()
    {
        // Arrange
        var formatter = CreateFormatter(FhirSdkMode.Ignixa);
        var bundle = new Bundle
        {
            Id = "bundle-summary-test",
            Type = Bundle.BundleType.Searchset,
            Total = 1,
            Entry =
            {
                new Bundle.EntryComponent
                {
                    Resource = new Patient
                    {
                        Id = "bundle-summary-patient",
                        BirthDate = "1980-01-01",
                        Contact =
                        {
                            new Patient.ContactComponent
                            {
                                Name = new HumanName { Family = "Hidden" },
                            },
                        },
                    },
                },
            },
        };
        var node = _ignixaSerializer.Parse(bundle.ToJson());

        // Act
        var json = await WriteObject(formatter, node, typeof(global::Ignixa.Serialization.SourceNodes.ResourceJsonNode), "?_summary=true");

        // Assert
        var parsed = Parser.Parse<Bundle>(json);
        Assert.Equal("bundle-summary-test", parsed.Id);
        Assert.Equal(Bundle.BundleType.Searchset, parsed.Type);
        Assert.Equal(1, parsed.Total);
        var entry = Assert.Single(parsed.Entry);
        var patient = Assert.IsType<Patient>(entry.Resource);
        Assert.Equal("bundle-summary-patient", patient.Id);
        Assert.Equal("1980-01-01", patient.BirthDate);
        Assert.Empty(patient.Contact);
        AssertContainsSubsettedTag(patient.Meta);
    }

    [Fact]
    public async Task GivenIgnixaModeAndSummaryDataProjection_WhenWritingBundleResourceJsonNode_ThenBundleShellDataFieldsAndEntryResourcesArePreserved()
    {
        // Arrange
        var formatter = CreateFormatter(FhirSdkMode.Ignixa);
        var bundle = CreateBundleWithEntryMetadata(includeNarrative: true);
        var node = _ignixaSerializer.Parse(bundle.ToJson());

        // Act
        var json = await WriteObject(formatter, node, typeof(global::Ignixa.Serialization.SourceNodes.ResourceJsonNode), "?_summary=data");

        // Assert
        var parsed = Parser.Parse<Bundle>(json);
        Assert.Equal(Bundle.BundleType.Searchset, parsed.Type);
        Assert.Equal(1, parsed.Total);
        Assert.Equal(new DateTimeOffset(2026, 7, 9, 7, 41, 26, TimeSpan.FromHours(-7)), parsed.Timestamp);
        var link = Assert.Single(parsed.Link);
        Assert.Equal("self", link.Relation);
        Assert.Equal("https://example.org/fhir/Patient?_id=entry-metadata-patient", link.Url);

        var entry = Assert.Single(parsed.Entry);
        Assert.Equal("https://example.org/fhir/Patient/entry-metadata-patient", entry.FullUrl);
        Assert.NotNull(entry.Search);
        Assert.NotNull(entry.Request);
        Assert.NotNull(entry.Response);
        var patient = Assert.IsType<Patient>(entry.Resource);
        Assert.True(patient.Active);
        Assert.NotEmpty(patient.Name);
        Assert.Null(patient.Text);
        AssertContainsSubsettedTag(patient.Meta);
    }

    [Fact]
    public async Task GivenIgnixaModeAndSummaryTextProjection_WhenWritingBundleResourceJsonNode_ThenEntryResourcesAreTextSummarizedAndTagged()
    {
        // Arrange
        var formatter = CreateFormatter(FhirSdkMode.Ignixa);
        var bundle = CreateBundleWithEntryMetadata(includeNarrative: true);
        var node = _ignixaSerializer.Parse(bundle.ToJson());

        // Act
        var json = await WriteObject(formatter, node, typeof(global::Ignixa.Serialization.SourceNodes.ResourceJsonNode), "?_summary=text");

        // Assert
        var parsed = Parser.Parse<Bundle>(json);
        Assert.Equal(Bundle.BundleType.Searchset, parsed.Type);
        var entry = Assert.Single(parsed.Entry);
        var patient = Assert.IsType<Patient>(entry.Resource);
        Assert.Equal("entry-metadata-patient", patient.Id);
        Assert.NotNull(patient.Text);
        Assert.Null(patient.ActiveElement);
        Assert.Empty(patient.Name);
        AssertContainsSubsettedTag(patient.Meta);

        var jsonObject = JObject.Parse(json);
        var resourceObject = (JObject)jsonObject["entry"]![0]!["resource"]!;
        Assert.Equal("Patient", (string)resourceObject["resourceType"]!);
        Assert.Equal("entry-metadata-patient", (string)resourceObject["id"]!);
        Assert.True(resourceObject.ContainsKey("meta"));
        Assert.True(resourceObject.ContainsKey("text"));
        Assert.False(resourceObject.ContainsKey("active"));
        Assert.False(resourceObject.ContainsKey("name"));
    }

    [Fact]
    public async Task GivenIgnixaModeAndSummaryTrueProjection_WhenBundleEntryHasMetadata_ThenEntryEnvelopeIsPreserved()
    {
        // Arrange
        var formatter = CreateFormatter(FhirSdkMode.Ignixa);
        var bundle = CreateBundleWithEntryMetadata();
        var node = _ignixaSerializer.Parse(bundle.ToJson());

        // Act
        var json = await WriteObject(formatter, node, typeof(global::Ignixa.Serialization.SourceNodes.ResourceJsonNode), "?_summary=true");

        // Assert
        var parsed = Parser.Parse<Bundle>(json);
        var entry = Assert.Single(parsed.Entry);
        Assert.Equal("https://example.org/fhir/Patient/entry-metadata-patient", entry.FullUrl);
        Assert.NotNull(entry.Search);
        Assert.Equal(Bundle.SearchEntryMode.Match, entry.Search.Mode);
        Assert.Equal(1.0m, entry.Search.Score);
        Assert.NotNull(entry.Request);
        Assert.Equal(Bundle.HTTPVerb.GET, entry.Request.Method);
        Assert.NotNull(entry.Response);
        Assert.Equal("200", entry.Response.Status);

        var patient = Assert.IsType<Patient>(entry.Resource);
        Assert.True(patient.Active);
        AssertContainsSubsettedTag(patient.Meta);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("name.family")]
    [InlineData("telecom.value")]
    [InlineData("extension")]
    public async Task GivenIgnixaModeAndElementsProjection_WhenNestedElementIsRequested_ThenRequestedElementIsPreserved(string element)
    {
        // Arrange
        var formatter = CreateFormatter(FhirSdkMode.Ignixa);
        var patient = new Patient
        {
            Id = "nested-elements-test",
            Active = true,
            Name = { new HumanName { Family = "Smith", Given = new[] { "John" } } },
            Telecom =
            {
                new ContactPoint
                {
                    System = ContactPoint.ContactPointSystem.Phone,
                    Value = "555-0100",
                },
            },
            Extension =
            {
                new Extension("http://example.org/fhir/StructureDefinition/projected", new FhirString("visible")),
            },
        };
        var node = _ignixaSerializer.Parse(patient.ToJson());

        // Act
        var json = await WriteObject(formatter, node, typeof(global::Ignixa.Serialization.SourceNodes.ResourceJsonNode), $"?_elements={element}");

        // Assert
        var parsed = Parser.Parse<Patient>(json);
        Assert.Equal("nested-elements-test", parsed.Id);

        switch (element)
        {
            case "name":
                Assert.Single(parsed.Name);
                Assert.Equal("Smith", parsed.Name[0].Family);
                Assert.Equal("John", parsed.Name[0].Given.Single());
                Assert.Empty(parsed.Telecom);
                Assert.Empty(parsed.Extension);
                break;
            case "name.family":
                Assert.Single(parsed.Name);
                Assert.Equal("Smith", parsed.Name[0].Family);
                Assert.Empty(parsed.Name[0].Given);
                Assert.Empty(parsed.Telecom);
                Assert.Empty(parsed.Extension);
                break;
            case "telecom.value":
                Assert.Empty(parsed.Name);
                Assert.Single(parsed.Telecom);
                Assert.Equal("555-0100", parsed.Telecom[0].Value);
                Assert.Null(parsed.Telecom[0].System);
                Assert.Empty(parsed.Extension);
                break;
            case "extension":
                Assert.Empty(parsed.Name);
                Assert.Empty(parsed.Telecom);
                Assert.Single(parsed.Extension);
                Assert.Equal("http://example.org/fhir/StructureDefinition/projected", parsed.Extension[0].Url);
                Assert.IsType<FhirString>(parsed.Extension[0].Value);
                break;
        }
    }

    [Fact]
    public async Task GivenIgnixaModeAndElementsProjection_WhenChoiceBaseElementIsRequested_ThenChoiceValueIsPreserved()
    {
        // Arrange
        var formatter = CreateFormatter(FhirSdkMode.Ignixa);
        var observation = new Observation
        {
            Id = "choice-elements-test",
            Status = ObservationStatus.Final,
            Code = new CodeableConcept("http://loinc.org", "8310-5"),
            Value = new Quantity(98.6m, "F"),
            Effective = new FhirDateTime("2026-07-08T22:10:54-07:00"),
        };
        var node = _ignixaSerializer.Parse(observation.ToJson());

        // Act
        var json = await WriteObject(formatter, node, typeof(global::Ignixa.Serialization.SourceNodes.ResourceJsonNode), "?_elements=value");

        // Assert
        var parsed = Parser.Parse<Observation>(json);
        Assert.Equal("choice-elements-test", parsed.Id);
        Assert.Equal(ObservationStatus.Final, parsed.Status);
        Assert.IsType<Quantity>(parsed.Value);
        Assert.Null(parsed.Effective);

        var jsonObject = JObject.Parse(json);
        Assert.True(jsonObject.ContainsKey("valueQuantity"));
        Assert.False(jsonObject.ContainsKey("effectiveDateTime"));
    }

    [Fact]
    public async Task GivenIgnixaModeAndElementsProjection_WhenPrimitiveElementHasExtension_ThenPrimitiveExtensionSiblingIsPreserved()
    {
        // Arrange
        var formatter = CreateFormatter(FhirSdkMode.Ignixa);
        var patient = new Patient
        {
            Id = "primitive-extension-test",
            ActiveElement = new FhirBoolean(true)
            {
                Extension =
                {
                    new Extension("http://example.org/fhir/StructureDefinition/active-note", new FhirString("visible")),
                },
            },
            Name = { new HumanName { Family = "Hidden" } },
        };
        var node = _ignixaSerializer.Parse(patient.ToJson());

        // Act
        var json = await WriteObject(formatter, node, typeof(global::Ignixa.Serialization.SourceNodes.ResourceJsonNode), "?_elements=active");

        // Assert
        var parsed = Parser.Parse<Patient>(json);
        Assert.Equal("primitive-extension-test", parsed.Id);
        Assert.True(parsed.Active);
        Assert.Single(parsed.ActiveElement.Extension);
        Assert.Empty(parsed.Name);

        var jsonObject = JObject.Parse(json);
        Assert.True(jsonObject.ContainsKey("active"));
        Assert.True(jsonObject.ContainsKey("_active"));
    }

    [Fact]
    public async Task GivenIgnixaModeAndElementsProjection_WhenNestedBackboneElementHasRequiredFields_ThenNestedRequiredFieldsArePreserved()
    {
        // Arrange
        var formatter = CreateFormatter(FhirSdkMode.Ignixa);
        var observation = new Observation
        {
            Id = "nested-required-test",
            Status = ObservationStatus.Final,
            Code = new CodeableConcept("http://loinc.org", "85354-9"),
            Component =
            {
                new Observation.ComponentComponent
                {
                    Code = new CodeableConcept("http://loinc.org", "8480-6"),
                    Value = new Quantity(120m, "mm[Hg]"),
                },
            },
        };
        var node = _ignixaSerializer.Parse(observation.ToJson());

        // Act
        var json = await WriteObject(formatter, node, typeof(global::Ignixa.Serialization.SourceNodes.ResourceJsonNode), "?_elements=component.value");

        // Assert
        var parsed = Parser.Parse<Observation>(json);
        Assert.Equal("nested-required-test", parsed.Id);
        Assert.Equal(ObservationStatus.Final, parsed.Status);
        Assert.NotNull(parsed.Code);
        var component = Assert.Single(parsed.Component);
        Assert.NotNull(component.Code);
        Assert.IsType<Quantity>(component.Value);
    }

    // ------------------------------------------------------------------
    // WriteResponseBody — Firely Resource POCO
    // ------------------------------------------------------------------

    [Fact]
    public async Task GivenAFirelyPatient_WhenWritten_ThenValidJsonIsProduced()
    {
        // Arrange
        var patient = new Patient
        {
            Id = "test-123",
            Active = true,
            Name = { new HumanName { Family = "Smith", Given = new[] { "John" } } },
        };

        // Act
        var json = await WriteResource(patient);

        // Assert — the output should be parseable by Firely and structurally equivalent
        Assert.False(string.IsNullOrEmpty(json));
        var parsed = Parser.Parse<Patient>(json);
        Assert.Equal("test-123", parsed.Id);
        Assert.Equal("Smith", parsed.Name[0].Family);
        Assert.Equal("John", parsed.Name[0].Given.First());
        Assert.Equal(true, parsed.Active);
    }

    [Fact]
    public async Task GivenAFirelyObservation_WhenWritten_ThenValidJsonIsProduced()
    {
        // Arrange
        var observation = Samples.GetDefaultObservation().ToPoco<Observation>();

        // Act
        var json = await WriteResource(observation);

        // Assert
        Assert.False(string.IsNullOrEmpty(json));
        var parsed = Parser.Parse<Observation>(json);
        Assert.Equal(observation.Id, parsed.Id);
    }

    // ------------------------------------------------------------------
    // WriteResponseBody — RawResourceElement (zero-copy path)
    // ------------------------------------------------------------------

    [Fact]
    public async Task GivenARawResourceElement_WhenWritten_ThenRawJsonIsPassedThrough()
    {
        // Arrange
        var rawElement = CreateRawPatientElement(new Patient { Id = "raw-test" });

        // Act
        var json = await WriteRawResourceElement(rawElement);

        // Assert — the raw JSON should be written directly
        Assert.False(string.IsNullOrEmpty(json));
        var parsed = Parser.Parse<Patient>(json);
        Assert.Equal("raw-test", parsed.Id);
    }

    [Fact]
    public async Task GivenIgnixaModeAndProjectionFallback_WhenWritingRawResourceElement_ThenInvalidOperationExceptionIsThrown()
    {
        // Arrange
        var formatter = CreateFormatter(FhirSdkMode.Ignixa);
        var rawElement = CreateRawPatientElement(new Patient { Id = "raw-projection-block", Active = true });

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => WriteObject(formatter, rawElement, typeof(RawResourceElement), "?_elements=active"));
        Assert.Contains("Firely fallback is not allowed in Ignixa SDK mode.", exception.Message);
    }

    [Fact]
    public async Task GivenHybridModeAndProjectionFallback_WhenWritingRawResourceElement_ThenProjectionIsPermitted()
    {
        // Arrange
        var rawElement = CreateRawPatientElement(new Patient
        {
            Id = "raw-projection-hybrid",
            Active = true,
            Name = { new HumanName { Family = "Smith" } },
        });

        // Act
        var json = await WriteObject(rawElement, typeof(RawResourceElement), "?_elements=active");

        // Assert
        var parsed = Parser.Parse<Patient>(json);
        Assert.True(parsed.Active);
        Assert.Empty(parsed.Name);
    }

    // ------------------------------------------------------------------
    // Pretty printing
    // ------------------------------------------------------------------

    [Fact]
    public async Task GivenAResource_WhenWrittenWithPrettyTrue_ThenOutputIsIndented()
    {
        // Arrange
        var patient = new Patient { Id = "pretty-test" };

        // Act
        var json = await WriteResource(patient, prettyQuery: "?_pretty=true");

        // Assert — indented JSON will contain newlines
        Assert.Contains("\n", json);
    }

    [Fact]
    public async Task GivenAResource_WhenWrittenWithoutPretty_ThenOutputIsCompact()
    {
        // Arrange
        var patient = new Patient { Id = "compact-test" };

        // Act
        var json = await WriteResource(patient);

        // Assert — compact JSON should not contain indentation newlines between properties
        Assert.DoesNotContain("\n  ", json);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private bool CanWrite(Type modelType)
    {
        var defaultHttpContext = new DefaultHttpContext();
        defaultHttpContext.Request.ContentType = "application/fhir+json";

        return _formatter.CanWriteResult(
            new OutputFormatterWriteContext(
                defaultHttpContext,
                Substitute.For<Func<Stream, Encoding, TextWriter>>(),
                modelType,
                null));
    }

    private async Task<string> WriteResource(Resource resource, string prettyQuery = null)
    {
        using var body = new MemoryStream();
        var httpContext = new DefaultHttpContext();
        httpContext.Response.StatusCode = (int)HttpStatusCode.OK;
        httpContext.Response.Body = body;

        if (prettyQuery != null)
        {
            httpContext.Request.QueryString = new QueryString(prettyQuery);
        }

        using var writer = new StringWriter();
        var writeContext = new OutputFormatterWriteContext(
            httpContext,
            (_, _) => writer,
            resource.GetType(),
            resource);

        await _formatter.WriteResponseBodyAsync(writeContext, Encoding.UTF8);

        body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(body);
        return await reader.ReadToEndAsync();
    }

    private async Task<string> WriteRawResourceElement(RawResourceElement rawElement)
    {
        return await WriteObject(rawElement, typeof(RawResourceElement));
    }

    private static RawResourceElement CreateRawPatientElement(Patient patient)
    {
        var rawJson = new FhirJsonSerializer().SerializeToString(patient);
        var wrapper = new ResourceWrapper(
            patient.ToResourceElement(),
            new RawResource(rawJson, FhirResourceFormat.Json, isMetaSet: true),
            null,
            false,
            null,
            null,
            null);

        return new RawResourceElement(wrapper);
    }

    private static Bundle CreateBundleWithEntryMetadata(bool includeNarrative = false)
    {
        return new Bundle
        {
            Id = "entry-metadata-bundle",
            Type = Bundle.BundleType.Searchset,
            Total = 1,
            Timestamp = new DateTimeOffset(2026, 7, 9, 7, 41, 26, TimeSpan.FromHours(-7)),
            Link =
            {
                new Bundle.LinkComponent
                {
                    Relation = "self",
                    Url = "https://example.org/fhir/Patient?_id=entry-metadata-patient",
                },
            },
            Entry =
            {
                new Bundle.EntryComponent
                {
                    FullUrl = "https://example.org/fhir/Patient/entry-metadata-patient",
                    Search = new Bundle.SearchComponent
                    {
                        Mode = Bundle.SearchEntryMode.Match,
                        Score = 1.0m,
                    },
                    Request = new Bundle.RequestComponent
                    {
                        Method = Bundle.HTTPVerb.GET,
                        Url = "Patient/entry-metadata-patient",
                    },
                    Response = new Bundle.ResponseComponent
                    {
                        Status = "200",
                    },
                    Resource = new Patient
                    {
                        Id = "entry-metadata-patient",
                        Active = true,
                        Text = includeNarrative
                            ? new Narrative
                            {
                                Status = Narrative.NarrativeStatus.Generated,
                                Div = "<div xmlns=\"http://www.w3.org/1999/xhtml\">Hidden narrative</div>",
                            }
                            : null,
                        Name = { new HumanName { Family = "Hidden" } },
                    },
                },
            },
        };
    }

    private static void AssertContainsSubsettedTag(Meta meta)
    {
        Assert.NotNull(meta);
        Assert.Contains(meta.Tag, tag =>
            string.Equals(tag.System, "http://terminology.hl7.org/CodeSystem/v3-ObservationValue", StringComparison.Ordinal) &&
            string.Equals(tag.Code, "SUBSETTED", StringComparison.Ordinal));
    }

    private async Task<string> WriteObject(object obj, Type objectType, string query = null)
    {
        return await WriteObject(_formatter, obj, objectType, query);
    }

    private async Task<string> WriteObject(IgnixaFhirJsonOutputFormatter formatter, object obj, Type objectType, string query = null)
    {
        using var body = new MemoryStream();
        var httpContext = new DefaultHttpContext();
        httpContext.Response.StatusCode = (int)HttpStatusCode.OK;
        httpContext.Response.Body = body;
        if (query != null)
        {
            httpContext.Request.QueryString = new QueryString(query);
        }

        using var writer = new StringWriter();
        var writeContext = new OutputFormatterWriteContext(
            httpContext,
            (_, _) => writer,
            objectType,
            obj);

        await formatter.WriteResponseBodyAsync(writeContext, Encoding.UTF8);

        body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(body);
        return await reader.ReadToEndAsync();
    }

    private IgnixaFhirJsonOutputFormatter CreateFormatter(FhirSdkMode mode)
    {
        var guard = new SdkFallbackGuard(
            new SdkModeProvider(new SdkConfiguration { Mode = mode }),
            NullLogger<SdkFallbackGuard>.Instance);

        return new IgnixaFhirJsonOutputFormatter(_ignixaSerializer, new FhirJsonSerializer(), ModelInfoProvider.Instance, guard);
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Ignixa;

/// <summary>
/// Verifies that JSON produced by Ignixa serialization can be round-tripped through
/// Firely parsing and that key properties are structurally equivalent.
/// This is the critical fidelity gate for the Ignixa migration.
/// </summary>
[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Serialization)]
public class IgnixaSerializationRoundTripTests
{
    private readonly IIgnixaJsonSerializer _ignixaSerializer;
    private readonly FhirJsonSerializer _firelySerializer;
    private readonly FhirJsonParser _firelyParser;

    public IgnixaSerializationRoundTripTests()
    {
        _ignixaSerializer = new IgnixaJsonSerializer();
        _firelySerializer = new FhirJsonSerializer();
        _firelyParser = new FhirJsonParser();
    }

    // ------------------------------------------------------------------
    // Firely → Ignixa → Firely round-trip
    // Ensures Ignixa can faithfully re-serialize Firely-produced JSON.
    // ------------------------------------------------------------------

    [Fact]
    public void GivenAPatient_WhenRoundTrippedThroughIgnixa_ThenKeyPropertiesArePreserved()
    {
        // Arrange
        var original = Samples.GetJsonSample("Patient").ToPoco<Patient>();

        // Act — Firely serialize → Ignixa parse → Ignixa serialize → Firely parse
        var roundTripped = RoundTripFirelyToIgnixaToFirely(original);

        // Assert
        Assert.Equal(original.Id, roundTripped.Id);
        Assert.Equal(original.BirthDate, roundTripped.BirthDate);
        Assert.Equal(original.Gender, roundTripped.Gender);
        Assert.Equal(original.Name.Count, roundTripped.Name.Count);

        for (int i = 0; i < original.Name.Count; i++)
        {
            Assert.Equal(original.Name[i].Family, roundTripped.Name[i].Family);
            Assert.Equal(original.Name[i].Given.ToList(), roundTripped.Name[i].Given.ToList());
        }
    }

    [Fact]
    public void GivenAnObservation_WhenRoundTrippedThroughIgnixa_ThenKeyPropertiesArePreserved()
    {
        // Arrange
        var original = Samples.GetDefaultObservation().ToPoco<Observation>();

        // Act
        var roundTripped = RoundTripFirelyToIgnixaToFirely(original);

        // Assert
        Assert.Equal(original.Id, roundTripped.Id);
        Assert.Equal(original.Status, roundTripped.Status);
        Assert.Equal(original.Code?.Coding?.Count ?? 0, roundTripped.Code?.Coding?.Count ?? 0);
    }

    [Fact]
    public void GivenACoverage_WhenRoundTrippedThroughIgnixa_ThenKeyPropertiesArePreserved()
    {
        // Arrange
        var original = Samples.GetDefaultCoverage().ToPoco<Coverage>();

        // Act
        var roundTripped = RoundTripFirelyToIgnixaToFirely(original);

        // Assert
        Assert.Equal(original.Id, roundTripped.Id);
        Assert.Equal(original.Status, roundTripped.Status);
    }

    [Fact]
    public void GivenAPractitioner_WhenRoundTrippedThroughIgnixa_ThenKeyPropertiesArePreserved()
    {
        // Arrange
        var original = Samples.GetDefaultPractitioner().ToPoco<Practitioner>();

        // Act
        var roundTripped = RoundTripFirelyToIgnixaToFirely(original);

        // Assert
        Assert.Equal(original.Id, roundTripped.Id);
        Assert.Equal(original.Name.Count, roundTripped.Name.Count);
    }

    [Fact]
    public void GivenAnOrganization_WhenRoundTrippedThroughIgnixa_ThenKeyPropertiesArePreserved()
    {
        // Arrange
        var original = Samples.GetDefaultOrganization().ToPoco<Organization>();

        // Act
        var roundTripped = RoundTripFirelyToIgnixaToFirely(original);

        // Assert
        Assert.Equal(original.Id, roundTripped.Id);
        Assert.Equal(original.Name, roundTripped.Name);
    }

    // ------------------------------------------------------------------
    // Ignixa → Ignixa round-trip (parse → serialize → parse)
    // Ensures Ignixa is internally consistent.
    // ------------------------------------------------------------------

    [Fact]
    public void GivenPatientJson_WhenParsedAndReserializedByIgnixa_ThenOutputIsConsistent()
    {
        // Arrange
        var originalJson = Samples.GetJson("Patient");

        // Act — Ignixa parse → Ignixa serialize → Ignixa parse → Ignixa serialize
        var node1 = _ignixaSerializer.Parse(originalJson);
        var json1 = _ignixaSerializer.Serialize(node1);
        var node2 = _ignixaSerializer.Parse(json1);
        var json2 = _ignixaSerializer.Serialize(node2);

        // Assert — two consecutive round-trips should produce identical output
        Assert.Equal(json1, json2);
    }

    [Fact]
    public void GivenObservationJson_WhenParsedAndReserializedByIgnixa_ThenOutputIsConsistent()
    {
        // Arrange
        var originalJson = Samples.GetJson("Weight");

        // Act
        var node1 = _ignixaSerializer.Parse(originalJson);
        var json1 = _ignixaSerializer.Serialize(node1);
        var node2 = _ignixaSerializer.Parse(json1);
        var json2 = _ignixaSerializer.Serialize(node2);

        // Assert
        Assert.Equal(json1, json2);
    }

    // ------------------------------------------------------------------
    // Byte-level fidelity: Ignixa output should be valid for Firely
    // ------------------------------------------------------------------

    [Fact]
    public void GivenPatientJson_WhenParsedByIgnixaAndSerializedToBytes_ThenFirelyCanParse()
    {
        // Arrange
        var originalJson = Samples.GetJson("Patient");

        // Act
        var node = _ignixaSerializer.Parse(originalJson);
        var bytes = _ignixaSerializer.SerializeToBytes(node);
        var jsonString = System.Text.Encoding.UTF8.GetString(bytes.Span);
        var parsed = _firelyParser.Parse<Patient>(jsonString);

        // Assert
        Assert.NotNull(parsed);
        Assert.NotNull(parsed.Id);
    }

    // ------------------------------------------------------------------
    // Edge cases
    // ------------------------------------------------------------------

    [Fact]
    public void GivenAResourceWithExtensions_WhenRoundTripped_ThenExtensionsArePreserved()
    {
        // Arrange
        var patient = new Patient
        {
            Id = "ext-test",
            Extension =
            {
                new Extension("http://example.org/fhir/StructureDefinition/test", new FhirString("test-value")),
            },
        };

        // Act
        var roundTripped = RoundTripFirelyToIgnixaToFirely(patient);

        // Assert
        Assert.Single(roundTripped.Extension);
        Assert.Equal("http://example.org/fhir/StructureDefinition/test", roundTripped.Extension[0].Url);
        Assert.Equal("test-value", ((FhirString)roundTripped.Extension[0].Value).Value);
    }

    [Fact]
    public void GivenAResourceWithMeta_WhenRoundTripped_ThenMetaIsPreserved()
    {
        // Arrange
        var patient = new Patient
        {
            Id = "meta-test",
            Meta = new Meta
            {
                VersionId = "42",
                Profile = new[] { "http://example.org/fhir/StructureDefinition/test-profile" },
                Tag = { new Coding("http://example.org/tags", "test-tag") },
            },
        };

        // Act
        var roundTripped = RoundTripFirelyToIgnixaToFirely(patient);

        // Assert
        Assert.Equal("42", roundTripped.Meta.VersionId);
        Assert.Single(roundTripped.Meta.Profile);
        Assert.Single(roundTripped.Meta.Tag);
        Assert.Equal("test-tag", roundTripped.Meta.Tag[0].Code);
    }

    [Fact]
    public void GivenAResourceWithContainedResources_WhenRoundTripped_ThenContainedArePreserved()
    {
        // Arrange
        var patient = new Patient
        {
            Id = "contained-test",
            Contained =
            {
                new Organization { Id = "org1", Name = "Test Org" },
            },
        };

        // Act
        var roundTripped = RoundTripFirelyToIgnixaToFirely(patient);

        // Assert
        Assert.Single(roundTripped.Contained);
        var org = Assert.IsType<Organization>(roundTripped.Contained[0]);
        Assert.Equal("org1", org.Id);
        Assert.Equal("Test Org", org.Name);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private T RoundTripFirelyToIgnixaToFirely<T>(T resource)
        where T : Resource
    {
        // Step 1: Firely serialize to JSON
        var firelyJson = _firelySerializer.SerializeToString(resource);

        // Step 2: Ignixa parse
        var ignixaNode = _ignixaSerializer.Parse(firelyJson);

        // Step 3: Ignixa serialize back to JSON
        var ignixaJson = _ignixaSerializer.Serialize(ignixaNode);

        // Step 4: Firely parse the Ignixa-produced JSON
        return _firelyParser.Parse<T>(ignixaJson);
    }
}

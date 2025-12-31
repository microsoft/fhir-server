// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.SourceNodes;
using Xunit;

namespace Microsoft.Health.Fhir.Ignixa.UnitTests;

public class IgnixaJsonSerializerTests
{
    private const string SamplePatientJson = """
        {
            "resourceType": "Patient",
            "id": "example",
            "meta": {
                "versionId": "1",
                "lastUpdated": "2024-01-15T10:30:00Z"
            },
            "active": true,
            "name": [
                {
                    "use": "official",
                    "family": "Smith",
                    "given": ["John", "Jacob"]
                }
            ],
            "gender": "male",
            "birthDate": "1990-05-15"
        }
        """;

    private readonly IIgnixaJsonSerializer _serializer = new IgnixaJsonSerializer();

    [Fact]
    public void Parse_ValidPatientJson_ReturnsResourceJsonNode()
    {
        // Act
        var resource = _serializer.Parse(SamplePatientJson);

        // Assert
        Assert.NotNull(resource);
        Assert.Equal("Patient", resource.ResourceType);
        Assert.Equal("example", resource.Id);
    }

    [Fact]
    public void Parse_ValidPatientJson_ExtractsMetaProperties()
    {
        // Act
        var resource = _serializer.Parse(SamplePatientJson);

        // Assert
        Assert.Equal("1", resource.Meta.VersionId);
        Assert.NotNull(resource.Meta.LastUpdated);
    }

    [Fact]
    public void Serialize_ResourceJsonNode_ReturnsValidJson()
    {
        // Arrange
        var resource = _serializer.Parse(SamplePatientJson);

        // Act
        var json = _serializer.Serialize(resource);

        // Assert
        Assert.NotNull(json);
        Assert.Contains("\"resourceType\":\"Patient\"", json);
        Assert.Contains("\"id\":\"example\"", json);
    }

    [Fact]
    public void Serialize_WithPrettyPrint_ReturnsFormattedJson()
    {
        // Arrange
        var resource = _serializer.Parse(SamplePatientJson);

        // Act
        var prettyJson = _serializer.Serialize(resource, pretty: true);
        var compactJson = _serializer.Serialize(resource, pretty: false);

        // Assert
        Assert.True(prettyJson.Length > compactJson.Length);
        Assert.Contains("\n", prettyJson);
    }

    [Fact]
    public void Parse_GenericType_ReturnsTypedResourceNode()
    {
        // Arrange - use Bundle which has typed node
        var bundleJson = """
            {
                "resourceType": "Bundle",
                "id": "test-bundle",
                "type": "collection",
                "entry": []
            }
            """;

        // Act
        var bundle = _serializer.Parse<ResourceJsonNode>(bundleJson);

        // Assert
        Assert.NotNull(bundle);
        Assert.Equal("Bundle", bundle.ResourceType);
    }

    [Fact]
    public void RoundTrip_PatientResource_PreservesData()
    {
        // Arrange
        var original = _serializer.Parse(SamplePatientJson);

        // Act
        var json = _serializer.Serialize(original);
        var roundTripped = _serializer.Parse(json);

        // Assert
        Assert.Equal(original.ResourceType, roundTripped.ResourceType);
        Assert.Equal(original.Id, roundTripped.Id);
        Assert.Equal(original.Meta.VersionId, roundTripped.Meta.VersionId);
    }

    [Fact]
    public void SerializeToBytes_ReturnsUtf8Bytes()
    {
        // Arrange
        var resource = _serializer.Parse(SamplePatientJson);

        // Act
        var bytes = _serializer.SerializeToBytes(resource);

        // Assert
        Assert.False(bytes.IsEmpty);
        var json = System.Text.Encoding.UTF8.GetString(bytes.Span);
        Assert.Contains("Patient", json);
    }

    [Fact]
    public async Task ParseAsync_FromStream_ReturnsResourceJsonNode()
    {
        // Arrange
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(SamplePatientJson));

        // Act
        var resource = await _serializer.ParseAsync(stream);

        // Assert
        Assert.NotNull(resource);
        Assert.Equal("Patient", resource.ResourceType);
        Assert.Equal("example", resource.Id);
    }
}

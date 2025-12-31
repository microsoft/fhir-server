// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Specification.Generated;
using Xunit;

namespace Microsoft.Health.Fhir.Ignixa.UnitTests;

public class IgnixaResourceElementTests
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
    private readonly R4CoreSchemaProvider _schemaProvider = new();

    [Fact]
    public void Constructor_WithValidResourceAndSchema_CreatesElement()
    {
        // Arrange
        var resourceNode = _serializer.Parse(SamplePatientJson);

        // Act
        var element = new IgnixaResourceElement(resourceNode, _schemaProvider);

        // Assert
        Assert.NotNull(element);
        Assert.Equal("Patient", element.InstanceType);
        Assert.Equal("example", element.Id);
    }

    [Fact]
    public void VersionId_ReturnsMetaVersionId()
    {
        // Arrange
        var resourceNode = _serializer.Parse(SamplePatientJson);
        var element = new IgnixaResourceElement(resourceNode, _schemaProvider);

        // Act
        var versionId = element.VersionId;

        // Assert
        Assert.Equal("1", versionId);
    }

    [Fact]
    public void LastUpdated_ReturnsMetaLastUpdated()
    {
        // Arrange
        var resourceNode = _serializer.Parse(SamplePatientJson);
        var element = new IgnixaResourceElement(resourceNode, _schemaProvider);

        // Act
        var lastUpdated = element.LastUpdated;

        // Assert
        Assert.NotNull(lastUpdated);
        Assert.Equal(2024, lastUpdated.Value.Year);
        Assert.Equal(1, lastUpdated.Value.Month);
        Assert.Equal(15, lastUpdated.Value.Day);
    }

    [Fact]
    public void IsDomainResource_ForPatient_ReturnsTrue()
    {
        // Arrange
        var resourceNode = _serializer.Parse(SamplePatientJson);
        var element = new IgnixaResourceElement(resourceNode, _schemaProvider);

        // Act
        var isDomainResource = element.IsDomainResource;

        // Assert
        Assert.True(isDomainResource);
    }

    [Fact]
    public void IsDomainResource_ForBundle_ReturnsFalse()
    {
        // Arrange
        var bundleJson = """{"resourceType": "Bundle", "id": "test", "type": "collection"}""";
        var resourceNode = _serializer.Parse(bundleJson);
        var element = new IgnixaResourceElement(resourceNode, _schemaProvider);

        // Act
        var isDomainResource = element.IsDomainResource;

        // Assert
        Assert.False(isDomainResource);
    }

    [Fact]
    public void Element_ReturnsSchemaAwareElement()
    {
        // Arrange
        var resourceNode = _serializer.Parse(SamplePatientJson);
        var element = new IgnixaResourceElement(resourceNode, _schemaProvider);

        // Act
        var schemaElement = element.Element;

        // Assert
        Assert.NotNull(schemaElement);
        Assert.Equal("Patient", schemaElement.InstanceType);
    }

    [Fact]
    public void ToTypedElement_ReturnsFirelyCompatibleElement()
    {
        // Arrange
        var resourceNode = _serializer.Parse(SamplePatientJson);
        var element = new IgnixaResourceElement(resourceNode, _schemaProvider);

        // Act
        var typedElement = element.ToTypedElement();

        // Assert
        Assert.NotNull(typedElement);
        Assert.Equal("Patient", typedElement.InstanceType);
    }

    [Fact]
    public void SetVersionId_UpdatesMetaVersionId()
    {
        // Arrange
        var resourceNode = _serializer.Parse(SamplePatientJson);
        var element = new IgnixaResourceElement(resourceNode, _schemaProvider);

        // Act
        element.SetVersionId("2");

        // Assert
        Assert.Equal("2", element.VersionId);
        Assert.Equal("2", resourceNode.Meta.VersionId);
    }

    [Fact]
    public void SetLastUpdated_UpdatesMetaLastUpdated()
    {
        // Arrange
        var resourceNode = _serializer.Parse(SamplePatientJson);
        var element = new IgnixaResourceElement(resourceNode, _schemaProvider);
        var newTimestamp = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);

        // Act
        element.SetLastUpdated(newTimestamp);

        // Assert
        Assert.Equal(newTimestamp, element.LastUpdated);
    }

    [Fact]
    public void InvalidateCaches_ClearsCachedElements()
    {
        // Arrange
        var resourceNode = _serializer.Parse(SamplePatientJson);
        var element = new IgnixaResourceElement(resourceNode, _schemaProvider);

        // Access element to populate cache
        _ = element.Element;
        _ = element.ToTypedElement();

        // Act - should not throw
        element.InvalidateCaches();

        // Assert - accessing again should work
        var newElement = element.Element;
        Assert.NotNull(newElement);
    }

    [Fact]
    public void ResourceNode_ReturnsSameUnderlyingNode()
    {
        // Arrange
        var resourceNode = _serializer.Parse(SamplePatientJson);
        var element = new IgnixaResourceElement(resourceNode, _schemaProvider);

        // Act
        var returnedNode = element.ResourceNode;

        // Assert
        Assert.Same(resourceNode, returnedNode);
    }
}

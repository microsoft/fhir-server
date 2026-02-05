// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage
{
    /// <summary>
    /// Unit tests for ResourceWrapperExtention.
    /// Tests the conversion of ResourceWrapper to ResourceDateKey.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class ResourceWrapperExtentionTests
    {
        [Fact]
        public void GivenResourceWrapper_WhenToResourceDateKey_ThenCreatesKeyWithCorrectResourceTypeId()
        {
            // Arrange
            var lastModified = DateTimeOffset.UtcNow;
            var wrapper = new ResourceWrapper(
                resourceId: "test-id-123",
                versionId: "1",
                resourceTypeName: "Patient",
                rawResource: new RawResource("{}",  FhirResourceFormat.Json, isMetaSet: false),
                request: new ResourceRequest("POST"),
                lastModified: lastModified,
                deleted: false,
                searchIndices: null,
                compartmentIndices: null,
                lastModifiedClaims: null,
                searchParameterHash: "hash");

            short expectedResourceTypeId = 103;
            Func<string, short> getResourceTypeId = (typeName) => typeName == "Patient" ? expectedResourceTypeId : (short)0;

            // Act
            var key = wrapper.ToResourceDateKey(getResourceTypeId);

            // Assert
            Assert.Equal(expectedResourceTypeId, key.ResourceTypeId);
            Assert.Equal("test-id-123", key.Id);
            Assert.Equal("1", key.VersionId);
        }

        [Fact]
        public void GivenResourceWrapper_WhenToResourceDateKeyWithIgnoreVersionTrue_ThenVersionIdIsNull()
        {
            // Arrange
            var lastModified = DateTimeOffset.UtcNow;
            var wrapper = new ResourceWrapper(
                resourceId: "resource-456",
                versionId: "5",
                resourceTypeName: "Observation",
                rawResource: new RawResource("{}", FhirResourceFormat.Json, isMetaSet: false),
                request: new ResourceRequest("PUT"),
                lastModified: lastModified,
                deleted: false,
                searchIndices: null,
                compartmentIndices: null,
                lastModifiedClaims: null,
                searchParameterHash: "hash");

            Func<string, short> getResourceTypeId = (typeName) => (short)200;

            // Act
            var key = wrapper.ToResourceDateKey(getResourceTypeId, ignoreVersion: true);

            // Assert
            Assert.Null(key.VersionId);
            Assert.Equal("resource-456", key.Id);
        }

        [Fact]
        public void GivenResourceWrapper_WhenToResourceDateKeyWithIgnoreVersionFalse_ThenVersionIdIsPreserved()
        {
            // Arrange
            var lastModified = DateTimeOffset.UtcNow;
            var wrapper = new ResourceWrapper(
                resourceId: "resource-789",
                versionId: "10",
                resourceTypeName: "Encounter",
                rawResource: new RawResource("{}", FhirResourceFormat.Json, isMetaSet: false),
                request: new ResourceRequest("PUT"),
                lastModified: lastModified,
                deleted: false,
                searchIndices: null,
                compartmentIndices: null,
                lastModifiedClaims: null,
                searchParameterHash: "hash");

            Func<string, short> getResourceTypeId = (typeName) => (short)150;

            // Act
            var key = wrapper.ToResourceDateKey(getResourceTypeId, ignoreVersion: false);

            // Assert
            Assert.Equal("10", key.VersionId);
        }

        [Fact]
        public void GivenResourceWrapper_WhenToResourceDateKey_ThenResourceSurrogateIdIsDerivedFromLastModified()
        {
            // Arrange
            var lastModified = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);
            var wrapper = new ResourceWrapper(
                resourceId: "test-id",
                versionId: "1",
                resourceTypeName: "Patient",
                rawResource: new RawResource("{}", FhirResourceFormat.Json, isMetaSet: false),
                request: new ResourceRequest("POST"),
                lastModified: lastModified,
                deleted: false,
                searchIndices: null,
                compartmentIndices: null,
                lastModifiedClaims: null,
                searchParameterHash: "hash");

            Func<string, short> getResourceTypeId = (typeName) => (short)1;

            // Act
            var key = wrapper.ToResourceDateKey(getResourceTypeId);

            // Assert - ResourceSurrogateId should be derived from lastModified using ToSurrogateId()
            var expectedSurrogateId = lastModified.ToSurrogateId();
            Assert.Equal(expectedSurrogateId, key.ResourceSurrogateId);
        }

        [Theory]
        [InlineData("Patient", 1)]
        [InlineData("Observation", 2)]
        [InlineData("Encounter", 3)]
        [InlineData("DiagnosticReport", 4)]
        public void GivenDifferentResourceTypes_WhenToResourceDateKey_ThenCallsGetResourceTypeIdWithCorrectType(string resourceTypeName, short expectedTypeId)
        {
            // Arrange
            var wrapper = new ResourceWrapper(
                resourceId: "id",
                versionId: "1",
                resourceTypeName: resourceTypeName,
                rawResource: new RawResource("{}", FhirResourceFormat.Json, isMetaSet: false),
                request: new ResourceRequest("POST"),
                lastModified: DateTimeOffset.UtcNow,
                deleted: false,
                searchIndices: null,
                compartmentIndices: null,
                lastModifiedClaims: null,
                searchParameterHash: "hash");

            string capturedTypeName = null;
            Func<string, short> getResourceTypeId = (typeName) =>
            {
                capturedTypeName = typeName;
                return expectedTypeId;
            };

            // Act
            var key = wrapper.ToResourceDateKey(getResourceTypeId);

            // Assert
            Assert.Equal(resourceTypeName, capturedTypeName);
            Assert.Equal(expectedTypeId, key.ResourceTypeId);
        }
    }
}

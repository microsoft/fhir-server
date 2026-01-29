// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlServerFhirDataStoreUnitTests
    {
        [Fact]
        public void RemoveTrailingZerosFromMillisecondsForAGivenDate_WithAllZeroMilliseconds_ShouldRemoveDotAndMilliseconds()
        {
            var date = new DateTimeOffset(2022, 3, 9, 1, 40, 52, 0, TimeSpan.FromHours(2));

            var result = InvokeRemoveTrailingZerosFromMillisecondsForAGivenDate(date);

            Assert.Equal("2022-03-09T01:40:52+02:00", result);
        }

        [Fact]
        public void RemoveTrailingZerosFromMillisecondsForAGivenDate_WithTrailingZeros_ShouldRemoveOnlyTrailingZeros()
        {
            var date = new DateTimeOffset(2022, 3, 9, 1, 40, 52, 69, TimeSpan.FromHours(2));

            var result = InvokeRemoveTrailingZerosFromMillisecondsForAGivenDate(date);

            Assert.Equal("2022-03-09T01:40:52.069+02:00", result);
        }

        [Fact]
        public void RemoveTrailingZerosFromMillisecondsForAGivenDate_WithNoTrailingZeros_ShouldReturnUnchanged()
        {
            var date = new DateTimeOffset(2022, 3, 9, 1, 40, 52, 123, TimeSpan.FromHours(2)).AddTicks(4567);

            var result = InvokeRemoveTrailingZerosFromMillisecondsForAGivenDate(date);

            Assert.Equal("2022-03-09T01:40:52.1234567+02:00", result);
        }

        [Fact]
        public void RemoveTrailingZerosFromMillisecondsForAGivenDate_WithSingleNonZeroDigit_ShouldReturnSingleDigit()
        {
            var date = new DateTimeOffset(2022, 3, 9, 1, 40, 52, 100, TimeSpan.FromHours(2));

            var result = InvokeRemoveTrailingZerosFromMillisecondsForAGivenDate(date);

            Assert.Equal("2022-03-09T01:40:52.1+02:00", result);
        }

        [Fact]
        public void RemoveTrailingZerosFromMillisecondsForAGivenDate_WithMultipleTrailingZeros_ShouldRemoveAll()
        {
            var date = new DateTimeOffset(2022, 3, 9, 1, 40, 52, 10, TimeSpan.FromHours(2));

            var result = InvokeRemoveTrailingZerosFromMillisecondsForAGivenDate(date);

            Assert.Equal("2022-03-09T01:40:52.01+02:00", result);
        }

        [Fact]
        public void RemoveTrailingZerosFromMillisecondsForAGivenDate_WithMiddleNonZeroDigit_ShouldPreservePattern()
        {
            var date = new DateTimeOffset(2022, 3, 9, 1, 40, 52, 101, TimeSpan.FromHours(2));

            var result = InvokeRemoveTrailingZerosFromMillisecondsForAGivenDate(date);

            Assert.Equal("2022-03-09T01:40:52.101+02:00", result);
        }

        [Fact]
        public void RemoveTrailingZerosFromMillisecondsForAGivenDate_WithNegativeOffset_ShouldHandleCorrectly()
        {
            var date = new DateTimeOffset(2022, 3, 9, 1, 40, 52, 18, TimeSpan.FromHours(-5));

            var result = InvokeRemoveTrailingZerosFromMillisecondsForAGivenDate(date);

            Assert.Equal("2022-03-09T01:40:52.018-05:00", result);
        }

        [Fact]
        public void ChangesAreOnlyInMetadata_WithNoMetaInEither_ShouldReturnTrue()
        {
            var inputWrapper = CreateResourceWrapper("{\"resourceType\":\"Patient\",\"id\":\"123\",\"active\":true}");
            var existingWrapper = CreateResourceWrapper("{\"resourceType\":\"Patient\",\"id\":\"123\",\"active\":true}");

            var result = InvokeChangesAreOnlyInMetadata(inputWrapper, existingWrapper);

            Assert.True(result);
        }

        [Fact]
        public void ChangesAreOnlyInMetadata_WithMetaInInputOnly_ShouldReturnFalse()
        {
            var inputWrapper = CreateResourceWrapper("{\"resourceType\":\"Patient\",\"meta\":{\"versionId\":\"2\"},\"id\":\"123\",\"active\":true}");
            var existingWrapper = CreateResourceWrapper("{\"resourceType\":\"Patient\",\"id\":\"123\",\"active\":true}");

            var result = InvokeChangesAreOnlyInMetadata(inputWrapper, existingWrapper);

            Assert.False(result);
        }

        [Fact]
        public void ChangesAreOnlyInMetadata_WithMetaInExistingOnly_ShouldReturnFalse()
        {
            var inputWrapper = CreateResourceWrapper("{\"resourceType\":\"Patient\",\"id\":\"123\",\"active\":true}");
            var existingWrapper = CreateResourceWrapper("{\"resourceType\":\"Patient\",\"meta\":{\"versionId\":\"1\"},\"id\":\"123\",\"active\":true}");

            var result = InvokeChangesAreOnlyInMetadata(inputWrapper, existingWrapper);

            Assert.False(result);
        }

        [Fact]
        public void ChangesAreOnlyInMetadata_WithDifferentMetaInBoth_ShouldReturnTrue()
        {
            var inputWrapper = CreateResourceWrapper("{\"resourceType\":\"Patient\",\"meta\":{\"versionId\":\"2\",\"lastUpdated\":\"2023-01-02T00:00:00Z\"},\"id\":\"123\",\"active\":true}");
            var existingWrapper = CreateResourceWrapper("{\"resourceType\":\"Patient\",\"meta\":{\"versionId\":\"1\",\"lastUpdated\":\"2023-01-01T00:00:00Z\"},\"id\":\"123\",\"active\":true}");

            var result = InvokeChangesAreOnlyInMetadata(inputWrapper, existingWrapper);

            Assert.True(result);
        }

        [Fact]
        public void ChangesAreOnlyInMetadata_WithContentChangeOutsideMeta_ShouldReturnFalse()
        {
            var inputWrapper = CreateResourceWrapper("{\"resourceType\":\"Patient\",\"meta\":{\"versionId\":\"2\"},\"id\":\"123\",\"active\":false}");
            var existingWrapper = CreateResourceWrapper("{\"resourceType\":\"Patient\",\"meta\":{\"versionId\":\"1\"},\"id\":\"123\",\"active\":true}");

            var result = InvokeChangesAreOnlyInMetadata(inputWrapper, existingWrapper);

            Assert.False(result);
        }

        [Fact]
        public void ChangesAreOnlyInMetadata_WithComplexMetaContent_ShouldReturnTrue()
        {
            var inputWrapper = CreateResourceWrapper("{\"resourceType\":\"Patient\",\"meta\":{\"versionId\":\"2\",\"lastUpdated\":\"2023-01-02T00:00:00Z\",\"profile\":[\"http://example.com/profile\"],\"tag\":[{\"system\":\"http://example.com\",\"code\":\"test\"}]},\"id\":\"123\",\"active\":true}");
            var existingWrapper = CreateResourceWrapper("{\"resourceType\":\"Patient\",\"meta\":{\"versionId\":\"1\"},\"id\":\"123\",\"active\":true}");

            var result = InvokeChangesAreOnlyInMetadata(inputWrapper, existingWrapper);

            Assert.True(result);
        }

        // Note: GetJsonValue tests require instance creation which has complex dependencies
        // This method is indirectly tested through integration tests (UpdateTests, FhirPathPatchTests)

        private static ResourceWrapper CreateResourceWrapper(string rawResourceData)
        {
            return new ResourceWrapper(
                "123",
                "1",
                "Patient",
                new RawResource(rawResourceData, FhirResourceFormat.Json, isMetaSet: true),
                null,
                DateTimeOffset.UtcNow,
                false,
                null,
                null,
                null,
                null);
        }

        private static string InvokeRemoveTrailingZerosFromMillisecondsForAGivenDate(DateTimeOffset date)
        {
            var method = typeof(SqlServerFhirDataStore).GetMethod(
                "RemoveTrailingZerosFromMillisecondsForAGivenDate",
                BindingFlags.NonPublic | BindingFlags.Static);

            if (method == null)
            {
                throw new InvalidOperationException("Method 'RemoveTrailingZerosFromMillisecondsForAGivenDate' not found");
            }

            return (string)method.Invoke(null, new object[] { date });
        }

        private static bool InvokeChangesAreOnlyInMetadata(ResourceWrapper inputWrapper, ResourceWrapper existingWrapper)
        {
            var method = typeof(SqlServerFhirDataStore).GetMethod(
                "ChangesAreOnlyInMetadata",
                BindingFlags.NonPublic | BindingFlags.Static);

            if (method == null)
            {
                throw new InvalidOperationException("Method 'ChangesAreOnlyInMetadata' not found");
            }

            return (bool)method.Invoke(null, new object[] { inputWrapper, existingWrapper });
        }

        // Note: GetJsonValue tests require instance creation which has complex dependencies
        // This method is indirectly tested through integration tests (UpdateTests, FhirPathPatchTests)
    }
}

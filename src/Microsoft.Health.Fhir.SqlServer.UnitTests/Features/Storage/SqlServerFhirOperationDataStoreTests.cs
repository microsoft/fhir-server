// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Reflection;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage
{
    /// <summary>
    /// Unit tests for SqlServerFhirOperationDataStore.
    /// Tests the business logic for determining legacy job IDs and ETag conversion.
    /// These tests focus on the static helper methods which contain the core business logic.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class SqlServerFhirOperationDataStoreTests
    {
        /// <summary>
        /// Tests the IsLegacyJob method with various job ID formats.
        /// Legacy jobs use GUID-like string IDs (cannot be parsed as long), while newer jobs use numeric (long) IDs.
        /// This is critical business logic for backward compatibility with old job records.
        /// </summary>
        [Theory]
        [InlineData("abc-123-def", true)] // GUID-like ID is legacy
        [InlineData("not-a-number", true)] // Non-numeric ID is legacy
        [InlineData("job-id-123", true)] // Mixed alphanumeric is legacy
        [InlineData("", true)] // Empty string cannot be parsed as long
        [InlineData("12345678-1234-1234-1234-123456789012", true)] // Full GUID format is legacy
        [InlineData("1", false)] // Numeric ID is not legacy
        [InlineData("12345", false)] // Multi-digit numeric ID is not legacy
        [InlineData("0", false)] // Zero is valid long, not legacy
        [InlineData("9223372036854775807", false)] // Max long value is not legacy
        [InlineData("-1", false)] // Negative number is valid long, not legacy
        public void GivenJobId_WhenIsLegacyJobCalled_ThenReturnsExpectedResult(string jobId, bool expectedIsLegacy)
        {
            // Use reflection to test private static method
            var methodInfo = typeof(SqlServerFhirOperationDataStore)
                .GetMethod("IsLegacyJob", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(methodInfo);

            var result = (bool)methodInfo.Invoke(null, new object[] { jobId });

            Assert.Equal(expectedIsLegacy, result);
        }

        /// <summary>
        /// Tests GetRowVersionAsEtag with a known row version value.
        /// SQL Server row versions are stored as 8-byte big-endian values. This method converts them to ETags
        /// for optimistic concurrency control. Proper endianness handling is critical for correct versioning.
        /// </summary>
        [Fact]
        public void GivenRowVersionBytes_WhenGetRowVersionAsEtagCalled_ThenReturnsValidWeakETag()
        {
            // Arrange - Create a known row version value
            // Row version is 8 bytes (64-bit) in SQL Server
            long expectedVersion = 12345678901234L;
            byte[] rowVersionBytes = BitConverter.GetBytes(expectedVersion);

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(rowVersionBytes); // SQL Server stores in big-endian
            }

            // Use reflection to test private static method
            var methodInfo = typeof(SqlServerFhirOperationDataStore)
                .GetMethod("GetRowVersionAsEtag", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(methodInfo);

            // Act
            var result = (WeakETag)methodInfo.Invoke(null, [rowVersionBytes]);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(expectedVersion.ToString(), result.VersionId);
        }

        /// <summary>
        /// Tests GetRowVersionAsEtag with a simple known value (1) in big-endian format.
        /// Verifies proper byte order handling - SQL Server stores row versions in big-endian.
        /// </summary>
        [Fact]
        public void GivenBigEndianRowVersionBytes_WhenGetRowVersionAsEtagCalled_ThenConvertsCorrectly()
        {
            // Arrange - Test with a simple known value in big-endian (as stored by SQL Server)
            byte[] rowVersionBytes = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 }; // 1 in big-endian

            // Use reflection to test private static method
            var methodInfo = typeof(SqlServerFhirOperationDataStore)
                .GetMethod("GetRowVersionAsEtag", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(methodInfo);

            // Act
            var result = (WeakETag)methodInfo.Invoke(null, [rowVersionBytes]);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("1", result.VersionId);
        }

        /// <summary>
        /// Tests GetRowVersionAsEtag with the maximum long value to ensure no overflow.
        /// Row versions can be very large, so edge case handling is important.
        /// </summary>
        [Fact]
        public void GivenMaxRowVersion_WhenGetRowVersionAsEtagCalled_ThenHandlesLargeValue()
        {
            // Arrange - Test with max value to ensure no overflow
            long maxValue = long.MaxValue;
            byte[] rowVersionBytes = BitConverter.GetBytes(maxValue);

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(rowVersionBytes);
            }

            // Use reflection to test private static method
            var methodInfo = typeof(SqlServerFhirOperationDataStore)
                .GetMethod("GetRowVersionAsEtag", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(methodInfo);

            // Act
            var result = (WeakETag)methodInfo.Invoke(null, [rowVersionBytes]);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(maxValue.ToString(), result.VersionId);
        }
    }
}

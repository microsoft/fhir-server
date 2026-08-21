// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Watchdogs
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class QueryStoreReadonlyReasonTests
    {
        [Theory]
        [InlineData(1, "database is in read-only mode")]
        [InlineData(2, "database is in single-user mode")]
        [InlineData(4, "database is in emergency mode")]
        [InlineData(8, "database is a secondary replica")]
        [InlineData(65536, "Query Store has reached its size limit (MAX_STORAGE_SIZE_MB)")]
        [InlineData(131072, "Query Store has reached the limit on the number of statements")]
        public void GivenASingleDocumentedBit_WhenDescribed_ThenReturnsThatReason(int readonlyReason, string expected)
        {
            // Act
            string description = QueryStoreDiagnosticsWatchdog.DescribeReadonlyReason(readonlyReason);

            // Assert
            Assert.Equal(expected, description);
        }

        [Fact]
        public void GivenACombinedMask_WhenDescribed_ThenReturnsEveryReasonInBitOrder()
        {
            // Arrange
            const int combinedMask = 8 | 65536 | 131072;

            // Act
            string description = QueryStoreDiagnosticsWatchdog.DescribeReadonlyReason(combinedMask);

            // Assert
            Assert.Equal(
                "database is a secondary replica, Query Store has reached its size limit (MAX_STORAGE_SIZE_MB), Query Store has reached the limit on the number of statements",
                description);
        }

        [Fact]
        public void GivenNoReason_WhenDescribed_ThenDistinguishesNotReportedFromNone()
        {
            // Act
            string notReported = QueryStoreDiagnosticsWatchdog.DescribeReadonlyReason(null);
            string none = QueryStoreDiagnosticsWatchdog.DescribeReadonlyReason(0);

            // Assert
            Assert.Equal("not reported", notReported);
            Assert.Equal("none", none);
        }

        [Fact]
        public void GivenAnUndocumentedBit_WhenDescribed_ThenReportsItAsUnrecognizedRatherThanEmpty()
        {
            // Act
            string description = QueryStoreDiagnosticsWatchdog.DescribeReadonlyReason(1 << 20);

            // Assert
            Assert.Equal("unrecognized reason", description);
        }

        [Fact]
        public void GivenTheSizeLimitBitCombinedWithAnUndocumentedBit_WhenDescribed_ThenStillNamesTheDocumentedReason()
        {
            // Act
            string description = QueryStoreDiagnosticsWatchdog.DescribeReadonlyReason(65536 | (1 << 20));

            // Assert
            Assert.Contains("size limit", description, StringComparison.Ordinal);
        }
    }
}

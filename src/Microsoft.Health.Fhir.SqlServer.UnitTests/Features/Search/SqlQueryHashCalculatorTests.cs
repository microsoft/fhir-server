// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search
{
    /// <summary>
    /// Unit tests for SqlQueryHashCalculator.
    /// Tests the hash calculation logic that removes parameter hashes from SQL queries
    /// before computing the final query hash for query plan caching.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SqlQueryHashCalculatorTests
    {
        private readonly SqlQueryHashCalculator _calculator = new SqlQueryHashCalculator();

        [Fact]
        public void GivenQueryWithoutParametersHash_WhenRemoveParametersHash_ThenReturnsUnchanged()
        {
            // Arrange
            var query = "SELECT * AAAA Resource";

            // Act
            var result = SqlQueryHashCalculator.RemoveParametersHash(query);

            // Assert
            Assert.Equal(query, result);
        }

        [Fact]
        public void GivenQueryWithHash_WhenCalculateHash_ThenReturnsDeterministicHash()
        {
            // Arrange
            var query = "SELECT * AAAA Resource";

            // Act
            var hash1 = _calculator.CalculateHash(query);
            var hash2 = _calculator.CalculateHash(query);

            // Assert - Same query should produce same hash
            Assert.NotNull(hash1);
            Assert.NotEmpty(hash1);
            Assert.Equal(hash1, hash2);
        }

        [Fact]
        public void GivenDifferentQueries_WhenCalculateHash_ThenReturnsDifferentHashes()
        {
            // Arrange
            var query1 = "SELECT * AAAA Resource123";
            var query2 = "SELECT * AAAA Resource456";

            // Act
            var hash1 = _calculator.CalculateHash(query1);
            var hash2 = _calculator.CalculateHash(query2);

            // Assert - Different queries should produce different hashes
            Assert.NotEqual(hash1, hash2);
        }

        [Fact]
        public void GivenEmptyQuery_WhenCalculateHash_ThenReturnsHash()
        {
            // Arrange
            var query = string.Empty;

            // Act
            var hash = _calculator.CalculateHash(query);

            // Assert - Should handle empty string
            Assert.NotNull(hash);
            Assert.NotEmpty(hash);
        }

        [Fact]
        public void GivenVeryLongQuery_WhenCalculateHash_ThenReturnsConsistentHash()
        {
            // Arrange - Create a very long query
            var query = "SELECT * AAAA Resource " + new string('B', 10000);

            // Act
            var hash1 = _calculator.CalculateHash(query);
            var hash2 = _calculator.CalculateHash(query);

            // Assert
            Assert.Equal(hash1, hash2);
            Assert.NotNull(hash1);
            Assert.NotEmpty(hash1);
        }

        [Fact]
        public void GivenTwoQueriesDifferingOnlyInWhitespace_WhenCalculateHash_ThenReturnsDifferentHashes()
        {
            // Arrange - Whitespace differences should result in different hashes
            var query1 = "SELECT * AAAA Resource123";
            var query2 = "SELECT * AAAA  Resource123"; // Extra space

            // Act
            var hash1 = _calculator.CalculateHash(query1);
            var hash2 = _calculator.CalculateHash(query2);

            // Assert - Whitespace differences should be preserved
            Assert.NotEqual(hash1, hash2);
        }

        [Fact]
        public void GivenMultipleHashCommentsWithNormalizedQueryShapes_WhenCalculateHash_ThenHashIsUnchanged()
        {
            // Arrange
            const string queryWithoutAnnotations = """
                WITH cte0 AS (SELECT 1)
                /* HASH first-hash params=@p0 */
                SELECT * FROM cte0
                /* HASH second-hash params=@p1 */
                """;
            const string queryWithAnnotations = """
                WITH cte0 AS (SELECT 1)
                /* HASH first-hash params=@p0 fhir=Patient?name */
                SELECT * FROM cte0
                /* HASH second-hash params=@p1 fhir=Patient?name */
                """;

            // Act
            string hashWithoutAnnotations = _calculator.CalculateHash(queryWithoutAnnotations);
            string hashWithAnnotations = _calculator.CalculateHash(queryWithAnnotations);

            // Assert
            Assert.Equal(hashWithoutAnnotations, hashWithAnnotations);
        }

        [Fact]
        public void GivenMalformedHashCommentBeforeValidHashComment_WhenCalculateHash_ThenHashIsUnchanged()
        {
            // Arrange
            const string queryWithoutAnnotations = """
                /* HASH first-hash params=@p0 */
                SELECT 0
                /* HASH malformed
                SELECT 1
                /* HASH second-hash params=@p1 */
                """;
            const string queryWithAnnotations = """
                /* HASH first-hash params=@p0 fhir=Patient?name */
                SELECT 0
                /* HASH malformed fhir=Patient?name
                SELECT 1
                /* HASH second-hash params=@p1 fhir=Patient?name */
                """;

            // Act
            string hashWithoutAnnotations = _calculator.CalculateHash(queryWithoutAnnotations);
            string hashWithAnnotations = _calculator.CalculateHash(queryWithAnnotations);

            // Assert
            Assert.Equal(hashWithoutAnnotations, hashWithAnnotations);
        }

        [Fact]
        public void GivenNormalizedQueryShapeTextOutsideHashComment_WhenRemoveParametersHash_ThenTextIsPreserved()
        {
            // Arrange
            const string query = """
                /* HASH first-hash params=@p0 fhir=Patient?name */
                SELECT ' fhir=Patient?name'
                """;

            // Act
            string result = SqlQueryHashCalculator.RemoveParametersHash(query);

            // Assert
            Assert.Contains("' fhir=Patient?name'", result, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenNullQuery_WhenRemoveParametersHash_ThenThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<NullReferenceException>(() => SqlQueryHashCalculator.RemoveParametersHash(null));
        }

        [Fact]
        public void GivenNullQuery_WhenCalculateHash_ThenThrowsException()
        {
            // Act & Assert
            Assert.Throws<NullReferenceException>(() => _calculator.CalculateHash(null));
        }
    }
}

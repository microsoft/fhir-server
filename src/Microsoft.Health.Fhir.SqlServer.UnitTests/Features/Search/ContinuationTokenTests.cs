// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search
{
    /// <summary>
    /// Unit tests for ContinuationToken.
    /// Tests serialization, deserialization, and property access logic.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ContinuationTokenTests
    {
        [Fact]
        public void GivenSingleElementArray_WhenCreatingToken_ThenResourceSurrogateIdIsAccessible()
        {
            // Arrange
            var tokens = new object[] { 12345L };

            // Act
            var continuationToken = new ContinuationToken(tokens);

            // Assert
            Assert.Equal(12345L, continuationToken.ResourceSurrogateId);
            Assert.Null(continuationToken.ResourceTypeId);
            Assert.Null(continuationToken.SortValue);
        }

        [Fact]
        public void GivenTwoElementArray_WhenCreatingToken_ThenResourceTypeIdIsAccessible()
        {
            // Arrange
            var tokens = new object[] { (short)103, 12345L };

            // Act
            var continuationToken = new ContinuationToken(tokens);

            // Assert
            Assert.Equal(12345L, continuationToken.ResourceSurrogateId);
            Assert.Equal((short)103, continuationToken.ResourceTypeId);
            Assert.Null(continuationToken.SortValue);
        }

        [Fact]
        public void GivenThreeElementArray_WhenCreatingToken_ThenSortValueIsAccessible()
        {
            // Arrange
            var tokens = new object[] { "sortValue", (short)103, 12345L };

            // Act
            var continuationToken = new ContinuationToken(tokens);

            // Assert
            Assert.Equal(12345L, continuationToken.ResourceSurrogateId);
            Assert.Equal((short)103, continuationToken.ResourceTypeId);
            Assert.Equal("sortValue", continuationToken.SortValue);
        }

        [Fact]
        public void GivenResourceTypeIdAsLong_WhenAccessing_ThenConvertsToShort()
        {
            // Arrange - Simulate JSON deserialization which creates longs
            var tokens = new object[] { 103L, 12345L }; // long instead of short

            // Act
            var continuationToken = new ContinuationToken(tokens);

            // Assert
            Assert.Equal((short)103, continuationToken.ResourceTypeId);
        }

        [Fact]
        public void GivenResourceTypeIdAsUnsupportedType_WhenAccessing_ThenReturnsNull()
        {
            // Arrange
            var tokens = new object[] { "not a short", 12345L };

            // Act
            var continuationToken = new ContinuationToken(tokens);

            // Assert
            Assert.Null(continuationToken.ResourceTypeId);
        }

        [Fact]
        public void GivenToken_WhenSettingResourceSurrogateId_ThenUpdatesValue()
        {
            // Arrange
            var tokens = new object[] { 12345L };
            var continuationToken = new ContinuationToken(tokens);

            // Act
            continuationToken.ResourceSurrogateId = 67890L;

            // Assert
            Assert.Equal(67890L, continuationToken.ResourceSurrogateId);
        }

        [Fact]
        public void GivenToken_WhenSettingResourceTypeId_ThenUpdatesValue()
        {
            // Arrange
            var tokens = new object[] { (short)103, 12345L };
            var continuationToken = new ContinuationToken(tokens);

            // Act
            continuationToken.ResourceTypeId = 105;

            // Assert
            Assert.Equal((short)105, continuationToken.ResourceTypeId);
        }

        [Fact]
        public void GivenToken_WhenCallingToJson_ThenReturnsValidJson()
        {
            // Arrange
            var tokens = new object[] { "sortValue", (short)103, 12345L };
            var continuationToken = new ContinuationToken(tokens);

            // Act
            var json = continuationToken.ToJson();

            // Assert
            Assert.NotNull(json);
            Assert.Contains("sortValue", json);
            Assert.Contains("103", json);
            Assert.Contains("12345", json);
        }

        [Fact]
        public void GivenToken_WhenCallingToString_ThenReturnsJson()
        {
            // Arrange
            var tokens = new object[] { 12345L };
            var continuationToken = new ContinuationToken(tokens);

            // Act
            var result = continuationToken.ToString();

            // Assert
            Assert.Equal(continuationToken.ToJson(), result);
        }

        [Fact]
        public void GivenNullString_WhenFromString_ThenReturnsNull()
        {
            // Act
            var result = ContinuationToken.FromString(null);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void GivenPlainLongString_WhenFromString_ThenCreatesTokenWithSingleElement()
        {
            // Arrange - Backward compatibility: plain long string
            var longString = "12345";

            // Act
            var token = ContinuationToken.FromString(longString);

            // Assert
            Assert.NotNull(token);
            Assert.Equal(12345L, token.ResourceSurrogateId);
            Assert.Null(token.ResourceTypeId);
            Assert.Null(token.SortValue);
        }

        [Fact]
        public void GivenJsonArrayString_WhenFromString_ThenDeserializesCorrectly()
        {
            // Arrange
            var json = "[\"sortValue\",103,12345]";

            // Act
            var token = ContinuationToken.FromString(json);

            // Assert
            Assert.NotNull(token);
            Assert.Equal(12345L, token.ResourceSurrogateId);
            Assert.Equal((short)103, token.ResourceTypeId);
            Assert.Equal("sortValue", token.SortValue);
        }

        [Fact]
        public void GivenInvalidJsonString_WhenFromString_ThenReturnsNull()
        {
            // Arrange
            var invalidJson = "not valid json {[}]";

            // Act
            var token = ContinuationToken.FromString(invalidJson);

            // Assert
            Assert.Null(token);
        }

        [Fact]
        public void GivenToken_WhenRoundTripSerialization_ThenPreservesValues()
        {
            // Arrange
            var originalTokens = new object[] { "sortValue", (short)103, 12345L };
            var originalToken = new ContinuationToken(originalTokens);

            // Act - Serialize and deserialize
            var json = originalToken.ToJson();
            var deserializedToken = ContinuationToken.FromString(json);

            // Assert
            Assert.NotNull(deserializedToken);
            Assert.Equal(originalToken.ResourceSurrogateId, deserializedToken.ResourceSurrogateId);
            Assert.Equal(originalToken.ResourceTypeId, deserializedToken.ResourceTypeId);
            Assert.Equal(originalToken.SortValue, deserializedToken.SortValue);
        }

        [Theory]
        [InlineData("[12345]", 12345L, null, null)]
        [InlineData("[103,12345]", 12345L, (short)103, null)]
        [InlineData("[\"sort\",103,12345]", 12345L, (short)103, "sort")]
        public void GivenVariousJsonFormats_WhenFromString_ThenParsesCorrectly(
            string json,
            long expectedSurrogateId,
            short? expectedResourceTypeId,
            string expectedSortValue)
        {
            // Act
            var token = ContinuationToken.FromString(json);

            // Assert
            Assert.NotNull(token);
            Assert.Equal(expectedSurrogateId, token.ResourceSurrogateId);
            Assert.Equal(expectedResourceTypeId, token.ResourceTypeId);
            Assert.Equal(expectedSortValue, token.SortValue);
        }

        [Fact]
        public void GivenEmptyString_WhenFromString_ThenReturnsNull()
        {
            // Act
            var token = ContinuationToken.FromString(string.Empty);

            // Assert
            Assert.Null(token);
        }

        [Fact]
        public void GivenWhitespaceString_WhenFromString_ThenReturnsNull()
        {
            // Act
            var token = ContinuationToken.FromString("   ");

            // Assert
            Assert.Null(token);
        }

        [Fact]
        public void GivenNegativeLong_WhenFromString_ThenReturnsNull()
        {
            // Arrange - Negative numbers should not parse (NumberStyles.None)
            var negativeString = "-12345";

            // Act
            var token = ContinuationToken.FromString(negativeString);

            // Assert
            Assert.Null(token);
        }

        [Fact]
        public void GivenLongWithLeadingZeros_WhenFromString_ThenParsesCorrectly()
        {
            // Arrange
            var stringWithZeros = "00012345";

            // Act
            var token = ContinuationToken.FromString(stringWithZeros);

            // Assert
            Assert.NotNull(token);
            Assert.Equal(12345L, token.ResourceSurrogateId);
        }

        [Fact]
        public void GivenVeryLargeLong_WhenFromString_ThenParsesCorrectly()
        {
            // Arrange
            var largeLong = long.MaxValue.ToString();

            // Act
            var token = ContinuationToken.FromString(largeLong);

            // Assert
            Assert.NotNull(token);
            Assert.Equal(long.MaxValue, token.ResourceSurrogateId);
        }

        [Fact]
        public void GivenJsonWithNullSortValue_WhenFromString_ThenHandlesNull()
        {
            // Arrange
            var json = "[null,103,12345]";

            // Act
            var token = ContinuationToken.FromString(json);

            // Assert
            Assert.NotNull(token);
            Assert.Null(token.SortValue);
            Assert.Equal((short)103, token.ResourceTypeId);
            Assert.Equal(12345L, token.ResourceSurrogateId);
        }
    }
}

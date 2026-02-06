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
    /// Unit tests for IncludesContinuationToken.
    /// Tests the parsing, validation, and serialization of includes continuation tokens.
    /// The token structure supports 3-7 elements with complex nested token handling.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class IncludesContinuationTokenTests
    {
        [Fact]
        public void GivenValidMinimalTokens_WhenCreated_ThenParsesSuccessfully()
        {
            // Arrange - Minimal valid token with 3 required elements
            short resourceTypeId = 10;
            long surrogateIdMin = 100;
            long surrogateIdMax = 200;
            var tokens = new object[] { resourceTypeId, surrogateIdMin, surrogateIdMax };

            // Act
            var token = new IncludesContinuationToken(tokens);

            // Assert
            Assert.Equal(resourceTypeId, token.MatchResourceTypeId);
            Assert.Equal(surrogateIdMin, token.MatchResourceSurrogateIdMin);
            Assert.Equal(surrogateIdMax, token.MatchResourceSurrogateIdMax);
            Assert.Null(token.IncludeResourceTypeId);
            Assert.Null(token.IncludeResourceSurrogateId);
            Assert.Null(token.SortQuerySecondPhase);
            Assert.Null(token.SecondPhaseContinuationToken);
        }

        [Fact]
        public void GivenValidFullTokens_WhenCreated_ThenParsesSuccessfully()
        {
            // Arrange - Full token with all 7 elements
            short matchResourceTypeId = 10;
            long matchSurrogateIdMin = 100;
            long matchSurrogateIdMax = 200;
            short includeResourceTypeId = 20;
            long includeSurrogateId = 150;
            bool sortQuerySecondPhase = true;
            var nestedToken = new IncludesContinuationToken(new object[] { (short)15, 300L, 400L });

            var tokens = new object[]
            {
                matchResourceTypeId,
                matchSurrogateIdMin,
                matchSurrogateIdMax,
                includeResourceTypeId,
                includeSurrogateId,
                sortQuerySecondPhase,
                nestedToken,
            };

            // Act
            var token = new IncludesContinuationToken(tokens);

            // Assert
            Assert.Equal(matchResourceTypeId, token.MatchResourceTypeId);
            Assert.Equal(matchSurrogateIdMin, token.MatchResourceSurrogateIdMin);
            Assert.Equal(matchSurrogateIdMax, token.MatchResourceSurrogateIdMax);
            Assert.Equal(includeResourceTypeId, token.IncludeResourceTypeId);
            Assert.Equal(includeSurrogateId, token.IncludeResourceSurrogateId);
            Assert.Equal(sortQuerySecondPhase, token.SortQuerySecondPhase);
            Assert.NotNull(token.SecondPhaseContinuationToken);
            Assert.Equal((short)15, token.SecondPhaseContinuationToken.MatchResourceTypeId);
        }

        [Fact]
        public void GivenInvalidTokenCount_WhenCreated_ThenThrowsArgumentException()
        {
            // Arrange - Only 2 tokens (minimum is 3)
            var tokens = new object[] { (short)10, 100L };

            // Act & Assert
            Assert.Throws<ArgumentException>(() => new IncludesContinuationToken(tokens));
        }

        [Fact]
        public void GivenInvalidTokenTypes_WhenCreated_ThenThrowsArgumentException()
        {
            // Arrange - Invalid type for first element (should be short)
            var tokens = new object[] { "invalid", 100L, 200L };

            // Act & Assert
            Assert.Throws<ArgumentException>(() => new IncludesContinuationToken(tokens));
        }

        [Fact]
        public void GivenValidToken_WhenSerialized_ThenDeserializesCorrectly()
        {
            // Arrange
            var originalTokens = new object[] { (short)10, 100L, 200L, (short)20, 150L };
            var originalToken = new IncludesContinuationToken(originalTokens);

            // Act - Serialize and deserialize
            var json = originalToken.ToJson();
            var deserializedToken = IncludesContinuationToken.FromString(json);

            // Assert
            Assert.NotNull(deserializedToken);
            Assert.Equal(originalToken.MatchResourceTypeId, deserializedToken.MatchResourceTypeId);
            Assert.Equal(originalToken.MatchResourceSurrogateIdMin, deserializedToken.MatchResourceSurrogateIdMin);
            Assert.Equal(originalToken.MatchResourceSurrogateIdMax, deserializedToken.MatchResourceSurrogateIdMax);
            Assert.Equal(originalToken.IncludeResourceTypeId, deserializedToken.IncludeResourceTypeId);
            Assert.Equal(originalToken.IncludeResourceSurrogateId, deserializedToken.IncludeResourceSurrogateId);
        }

        [Fact]
        public void GivenNestedSecondPhaseToken_WhenCreated_ThenParsesNestedToken()
        {
            // Arrange - Token with nested second phase token
            var nestedTokens = new object[] { (short)15, 300L, 400L };
            var nestedToken = new IncludesContinuationToken(nestedTokens);
            var tokens = new object[] { (short)10, 100L, 200L, (short)20, 150L, true, nestedToken };

            // Act
            var token = new IncludesContinuationToken(tokens);

            // Assert
            Assert.NotNull(token.SecondPhaseContinuationToken);
            Assert.Equal((short)15, token.SecondPhaseContinuationToken.MatchResourceTypeId);
            Assert.Equal(300L, token.SecondPhaseContinuationToken.MatchResourceSurrogateIdMin);
            Assert.Equal(400L, token.SecondPhaseContinuationToken.MatchResourceSurrogateIdMax);
        }

        [Fact]
        public void GivenMinMaxOutOfOrder_WhenCreated_ThenSortsCorrectly()
        {
            // Arrange - Min and Max are swapped
            var tokens = new object[] { (short)10, 200L, 100L };

            // Act
            var token = new IncludesContinuationToken(tokens);

            // Assert - Should automatically sort them
            Assert.Equal(100L, token.MatchResourceSurrogateIdMin);
            Assert.Equal(200L, token.MatchResourceSurrogateIdMax);
        }

        [Fact]
        public void GivenNullJson_WhenFromString_ThenReturnsNull()
        {
            // Act
            var token = IncludesContinuationToken.FromString(null);

            // Assert
            Assert.Null(token);
        }

        [Fact]
        public void GivenInvalidJson_WhenFromString_ThenReturnsNull()
        {
            // Arrange
            var invalidJson = "{ invalid json }";

            // Act
            var token = IncludesContinuationToken.FromString(invalidJson);

            // Assert
            Assert.Null(token);
        }

        [Fact]
        public void GivenValidJson_WhenFromString_ThenReturnsValidToken()
        {
            // Arrange
            var originalToken = new IncludesContinuationToken(new object[] { (short)10, 100L, 200L });
            var json = originalToken.ToJson();

            // Act
            var parsedToken = IncludesContinuationToken.FromString(json);

            // Assert
            Assert.NotNull(parsedToken);
            Assert.Equal(10, parsedToken.MatchResourceTypeId);
            Assert.Equal(100L, parsedToken.MatchResourceSurrogateIdMin);
            Assert.Equal(200L, parsedToken.MatchResourceSurrogateIdMax);
        }

        [Fact]
        public void GivenTokenWithSortPhase_WhenCreated_ThenPreservesPhaseFlag()
        {
            // Arrange - Token with sort phase flag
            var tokens = new object[] { (short)10, 100L, 200L, (short)20, 150L, false };

            // Act
            var token = new IncludesContinuationToken(tokens);

            // Assert
            Assert.False(token.SortQuerySecondPhase);
        }

        [Fact]
        public void GivenTokensWith5Elements_WhenCreated_ThenParsesSuccessfully()
        {
            // Arrange - 5 elements: required 3 + optional include type + surrogate id
            var tokens = new object[] { (short)10, 100L, 200L, (short)20, 150L };

            // Act
            var token = new IncludesContinuationToken(tokens);

            // Assert
            Assert.Equal((short)10, token.MatchResourceTypeId);
            Assert.Equal(100L, token.MatchResourceSurrogateIdMin);
            Assert.Equal(200L, token.MatchResourceSurrogateIdMax);
            Assert.NotNull(token.IncludeResourceTypeId);
            Assert.Equal((short)20, token.IncludeResourceTypeId!.Value);
            Assert.NotNull(token.IncludeResourceSurrogateId);
            Assert.Equal(150L, token.IncludeResourceSurrogateId!.Value);
            Assert.Null(token.SortQuerySecondPhase);
            Assert.Null(token.SecondPhaseContinuationToken);
        }

        [Fact]
        public void GivenTokensWith6Elements_WhenCreated_ThenParsesSuccessfully()
        {
            // Arrange - 6 elements: 5 + sort phase flag
            var tokens = new object[] { (short)10, 100L, 200L, (short)20, 150L, true };

            // Act
            var token = new IncludesContinuationToken(tokens);

            // Assert
            Assert.Equal((short)10, token.MatchResourceTypeId);
            Assert.NotNull(token.IncludeResourceTypeId);
            Assert.Equal((short)20, token.IncludeResourceTypeId!.Value);
            Assert.NotNull(token.IncludeResourceSurrogateId);
            Assert.Equal(150L, token.IncludeResourceSurrogateId!.Value);
            Assert.NotNull(token.SortQuerySecondPhase);
            Assert.True(token.SortQuerySecondPhase!.Value);
            Assert.Null(token.SecondPhaseContinuationToken);
        }

        [Fact]
        public void GivenTokensWith4Elements_WhenCreated_ThenThrowsArgumentException()
        {
            // Arrange - 4 elements is invalid (need either 3, 5, 6, or 7)
            var tokens = new object[] { (short)10, 100L, 200L, (short)20 };

            // Act & Assert - Should fail because if token[3] exists, token[4] is required
            Assert.Throws<ArgumentException>(() => new IncludesContinuationToken(tokens));
        }

        [Fact]
        public void GivenTokenWithStringifiedNestedToken_WhenCreated_ThenConvertsToString()
        {
            // Arrange - Nested token is already stringified (happens in constructor)
            var nestedToken = new IncludesContinuationToken(new object[] { (short)15, 300L, 400L });
            var tokens = new object[] { (short)10, 100L, 200L, (short)20, 150L, true, nestedToken };

            // Act
            var token = new IncludesContinuationToken(tokens);
            var json = token.ToJson();

            // Assert - Should serialize without error
            Assert.NotNull(json);
            Assert.NotEmpty(json);
        }

        [Fact]
        public void GivenNullTokenArray_WhenCreated_ThenThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new IncludesContinuationToken(null));
        }

        [Fact]
        public void GivenTokenWithNullIncludeTypeId_WhenCreated_ThenIncludeIdsAreNull()
        {
            // Arrange - Include type ID is null (TryParse will fail and set to null)
            var tokens = new object[] { (short)10, 100L, 200L, null, 150L };

            // Act
            var token = new IncludesContinuationToken(tokens);

            // Assert - TryParse handles null gracefully, setting values to null
            Assert.Equal((short)10, token.MatchResourceTypeId);
            Assert.Null(token.IncludeResourceTypeId);
            Assert.NotNull(token.IncludeResourceSurrogateId);
            Assert.Equal(150L, token.IncludeResourceSurrogateId!.Value);
        }

        [Fact]
        public void GivenComplexNestedToken_WhenSerializedAndDeserialized_ThenPreservesStructure()
        {
            // Arrange - Create a complex nested structure
            var innerNestedToken = new IncludesContinuationToken(new object[] { (short)25, 500L, 600L });
            var nestedToken = new IncludesContinuationToken(new object[] { (short)15, 300L, 400L, (short)30, 350L, false, innerNestedToken });
            var outerToken = new IncludesContinuationToken(new object[] { (short)10, 100L, 200L, (short)20, 150L, true, nestedToken });

            // Act - Serialize and deserialize
            var json = outerToken.ToJson();
            var deserializedToken = IncludesContinuationToken.FromString(json);

            // Assert
            Assert.NotNull(deserializedToken);
            Assert.Equal(10, deserializedToken.MatchResourceTypeId);
            Assert.True(deserializedToken.SortQuerySecondPhase);
            Assert.NotNull(deserializedToken.SecondPhaseContinuationToken);
            Assert.Equal(15, deserializedToken.SecondPhaseContinuationToken.MatchResourceTypeId);
            Assert.False(deserializedToken.SecondPhaseContinuationToken.SortQuerySecondPhase);
            Assert.NotNull(deserializedToken.SecondPhaseContinuationToken.SecondPhaseContinuationToken);
            Assert.Equal(25, deserializedToken.SecondPhaseContinuationToken.SecondPhaseContinuationToken.MatchResourceTypeId);
        }

        [Fact]
        public void GivenTokenWithEqualMinMax_WhenCreated_ThenAcceptsEqualValues()
        {
            // Arrange - Min and Max are equal
            var tokens = new object[] { (short)10, 150L, 150L };

            // Act
            var token = new IncludesContinuationToken(tokens);

            // Assert
            Assert.Equal(150L, token.MatchResourceSurrogateIdMin);
            Assert.Equal(150L, token.MatchResourceSurrogateIdMax);
        }

        [Fact]
        public void GivenTokenToString_WhenCalled_ThenReturnsJson()
        {
            // Arrange
            var token = new IncludesContinuationToken(new object[] { (short)10, 100L, 200L });

            // Act
            var result = token.ToString();

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
            Assert.Equal(token.ToJson(), result);
        }
    }
}

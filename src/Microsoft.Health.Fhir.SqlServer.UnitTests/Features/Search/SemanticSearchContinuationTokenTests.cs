// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SemanticSearchContinuationTokenTests
    {
        [Theory]
        [InlineData(0.125, 103, 12345L)]
        [InlineData(-0.125, -1, -1L)]
        [InlineData(0.0, 103, 0L)]
        [InlineData(3.0, 103, 0L)]
        [InlineData(double.MaxValue, short.MaxValue, long.MaxValue)]
        [InlineData(double.MinValue, short.MinValue, long.MinValue)]
        public void GivenValidValues_WhenCreated_ThenAllPaginationKeysArePreserved(
            double distance,
            short resourceTypeId,
            long resourceSurrogateId)
        {
            // Arrange
            SemanticSearchContinuationToken continuationToken;

            // Act
            bool result = SemanticSearchContinuationToken.TryCreate(
                distance, resourceTypeId, resourceSurrogateId, out continuationToken);

            // Assert
            Assert.True(result);
            Assert.NotNull(continuationToken);
            Assert.Equal(distance, continuationToken.Distance);
            Assert.Equal(resourceTypeId, continuationToken.ResourceTypeId);
            Assert.Equal(resourceSurrogateId, continuationToken.ResourceSurrogateId);
        }

        [Theory]
        [InlineData(double.NaN, 103)]
        [InlineData(double.PositiveInfinity, 103)]
        [InlineData(double.NegativeInfinity, 103)]
        [InlineData(0.125, 0)]
        public void GivenInvalidValues_WhenCreated_ThenNoTokenIsProduced(double distance, short resourceTypeId)
        {
            // Arrange
            const long resourceSurrogateId = 12345L;

            // Act
            bool result = SemanticSearchContinuationToken.TryCreate(
                distance, resourceTypeId, resourceSurrogateId, out SemanticSearchContinuationToken continuationToken);

            // Assert
            Assert.False(result);
            Assert.Null(continuationToken);
        }
    }
}

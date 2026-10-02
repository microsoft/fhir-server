// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SqlSearchOptionsTests
    {
        [Fact]
        public void GivenNoSemanticContinuationToken_WhenOptionsAreCloned_ThenTokenRemainsAbsent()
        {
            // Arrange
            var options = new SqlSearchOptions(new SearchOptions
            {
                SearchParameters = [],
                UnsupportedSearchParams = [],
                Sort = [],
            });

            // Act
            SqlSearchOptions clone = options.CloneSqlSearchOptions();

            // Assert
            Assert.Null(options.SemanticContinuationToken);
            Assert.Null(clone.SemanticContinuationToken);
        }

        [Fact]
        public void GivenSemanticContinuationToken_WhenCloneIsChanged_ThenOriginalTokenIsPreserved()
        {
            // Arrange
            Assert.True(SemanticSearchContinuationToken.TryCreate(0.125, 103, 12345, out SemanticSearchContinuationToken original));
            Assert.True(SemanticSearchContinuationToken.TryCreate(0.25, 103, 12346, out SemanticSearchContinuationToken replacement));
            var options = new SqlSearchOptions(new SearchOptions
            {
                SearchParameters = [],
                UnsupportedSearchParams = [],
                Sort = [],
            })
            {
                SemanticContinuationToken = original,
            };

            // Act
            SqlSearchOptions clone = options.CloneSqlSearchOptions();
            SemanticSearchContinuationToken copied = clone.SemanticContinuationToken;
            clone.SemanticContinuationToken = replacement;

            // Assert
            Assert.Same(original, copied);
            Assert.Same(original, options.SemanticContinuationToken);
            Assert.Same(replacement, clone.SemanticContinuationToken);
        }
    }
}

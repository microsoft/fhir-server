// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.CosmosDb.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class CosmosDbSearchParameterValidatorTests
    {
        private readonly CosmosDbSearchParameterValidator _validator = new();

        [Fact]
        public void GivenAVectorSearchParameter_WhenSearchParameterIsValidated_ThenItIsRejectedBecauseSemanticSearchIsSqlOnly()
        {
            var searchParameter = new SearchParameterInfo(
                "test",
                "test",
                ValueSets.SearchParamType.Special,
                vectorConfig: new VectorSearchParameterConfig());

            bool result = _validator.ValidateSearchParameter(searchParameter, out string errorMessage);

            Assert.False(result);
            Assert.Contains("SQL Server", errorMessage);
        }

        [Fact]
        public void GivenAnOrdinarySearchParameter_WhenSearchParameterIsValidated_ThenItIsAccepted()
        {
            var searchParameter = new SearchParameterInfo("test", "test", ValueSets.SearchParamType.String);

            bool result = _validator.ValidateSearchParameter(searchParameter, out string errorMessage);

            Assert.True(result);
            Assert.Null(errorMessage);
        }
    }
}

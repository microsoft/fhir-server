// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.CosmosDb.Features.Search.Queries;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class QueryBuilderTests
    {
        [Fact]
        public void GivenSoftDeletedOnlySearch_WhenQueryBuilt_ThenOnlyDeletedResourcesAreSelected()
        {
            var searchOptions = new SearchOptions
            {
                ResourceVersionTypes = ResourceVersionType.SoftDeleted,
                Sort = [],
            };

            string query = new QueryBuilder().BuildSqlQuerySpec(searchOptions).QueryText;

            Assert.Contains("r.isDeleted =", query);
            Assert.DoesNotContain("r.isHistory =", query);
        }
    }
}

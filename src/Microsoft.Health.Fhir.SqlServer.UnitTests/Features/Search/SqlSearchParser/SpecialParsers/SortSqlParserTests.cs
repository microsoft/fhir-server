// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.SpecialParsers;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.SqlSearchParser.SpecialParsers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SortSqlParserTests
    {
        [Fact]
        public void GivenNoSortValue_WhenCreateOrderByClause_ThenReturnsDefaultOrder()
        {
            var result = SortSqlParser.CreateOrderByClause(sortDescending: false, hasSortValue: false);

            Assert.Contains("t.IsMatch DESC", result);
            Assert.Contains("t.ResourceTypeId ASC", result);
            Assert.Contains("t.ResourceSurrogateId ASC", result);
        }

        [Fact]
        public void GivenAscendingSort_WhenCreateOrderByClause_ThenReturnsAscendingSortWithNullsLast()
        {
            var result = SortSqlParser.CreateOrderByClause(sortDescending: false, hasSortValue: true);

            Assert.Contains("t.IsMatch DESC", result);
            Assert.Contains("CASE WHEN t.SortValue IS NULL THEN 1 ELSE 0 END ASC", result);
            Assert.Contains("t.SortValue ASC", result);
            Assert.Contains("t.ResourceTypeId ASC", result);
            Assert.Contains("t.ResourceSurrogateId ASC", result);
        }

        [Fact]
        public void GivenDescendingSort_WhenCreateOrderByClause_ThenReturnsDescendingSortWithNullsLast()
        {
            var result = SortSqlParser.CreateOrderByClause(sortDescending: true, hasSortValue: true);

            Assert.Contains("t.SortValue DESC", result);
            Assert.Contains("CASE WHEN t.SortValue IS NULL THEN 1 ELSE 0 END ASC", result);
        }

        [Fact]
        public void GivenNullSortParameterName_WhenCreateSortCte_ThenReturnsNull()
        {
            var parser = new SortSqlParser(ParserTestHelper.CreateMockDefinitionManager());
            var result = parser.CreateSortCte(null, false, "cte0", "sortCte", 1);

            Assert.Null(result);
        }

        [Fact]
        public void GivenEmptySourceCteName_WhenCreateSortCte_ThenReturnsNull()
        {
            var parser = new SortSqlParser(ParserTestHelper.CreateMockDefinitionManager());
            var result = parser.CreateSortCte("date", false, string.Empty, "sortCte", 1);

            Assert.Null(result);
        }
    }
}

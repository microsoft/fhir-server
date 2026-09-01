// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.SpecialParsers;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.SqlSearchParser.SpecialParsers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SystemSqlParserTests
    {
        private readonly SystemSqlParser _parser = new SystemSqlParser();

        [Fact]
        public void GivenNoResourceTypes_WhenParse_ThenProducesBasicQuery()
        {
            var options = new ParserOptions
            {
                CteNumber = 0,
                SqlQueryBuilder = new SqlQueryBuilder(),
            };

            _parser.Parse(string.Empty, string.Empty, options);
            var sql = options.SqlQueryBuilder.ToString();

            Assert.Contains("SELECT r.ResourceTypeId, r.ResourceSurrogateId", sql);
            Assert.Contains("FROM dbo.Resource", sql);
            Assert.Contains("r.IsHistory = 0", sql);
            Assert.Contains("r.IsDeleted = 0", sql);
        }

        [Fact]
        public void GivenResourceTypes_WhenParse_ThenAddsResourceTypeFilter()
        {
            var options = new ParserOptions
            {
                CteNumber = 0,
                SqlQueryBuilder = new SqlQueryBuilder(),
                ResourceTypes = new List<short> { 10, 20 },
            };

            _parser.Parse(string.Empty, string.Empty, options);
            var sql = options.SqlQueryBuilder.ToString();

            Assert.Contains("r.ResourceTypeId IN (10, 20)", sql);
        }

        [Fact]
        public void GivenParse_WhenCalled_ThenCreatesCte()
        {
            var options = new ParserOptions
            {
                CteNumber = 5,
                SqlQueryBuilder = new SqlQueryBuilder(),
            };

            _parser.Parse(string.Empty, string.Empty, options);
            var sql = options.SqlQueryBuilder.ToString();

            Assert.Contains("cte5 AS (", sql);
        }
    }
}

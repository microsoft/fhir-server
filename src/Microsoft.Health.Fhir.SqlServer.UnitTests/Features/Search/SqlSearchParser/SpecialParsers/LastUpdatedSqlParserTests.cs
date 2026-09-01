// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
    public class LastUpdatedSqlParserTests
    {
        private readonly LastUpdatedSqlParser _parser = new LastUpdatedSqlParser();

        private ParserOptions CreateOptions(int cteNumber = 0)
        {
            return new ParserOptions
            {
                CteNumber = cteNumber,
                SqlQueryBuilder = new SqlQueryBuilder(),
                ResourceTypes = new List<short> { 1 },
            };
        }

        [Fact]
        public void GivenDateValue_WhenParse_ThenProducesSurrogateIdRangeCondition()
        {
            var options = CreateOptions();
            _parser.Parse("_lastUpdated", "2024-01-15", options);
            var sql = options.SqlQueryBuilder.ToString();

            Assert.Contains("r.ResourceSurrogateId >=", sql);
            Assert.Contains("r.ResourceSurrogateId <", sql);
        }

        [Fact]
        public void GivenGtPrefix_WhenParse_ThenProducesGreaterThanOrEqualCondition()
        {
            var options = CreateOptions();
            _parser.Parse("_lastUpdated", "gt2024-01-15", options);
            var sql = options.SqlQueryBuilder.ToString();

            Assert.Contains("r.ResourceSurrogateId >=", sql);
        }

        [Fact]
        public void GivenLtPrefix_WhenParse_ThenProducesLessThanCondition()
        {
            var options = CreateOptions();
            _parser.Parse("_lastUpdated", "lt2024-01-15", options);
            var sql = options.SqlQueryBuilder.ToString();

            Assert.Contains("r.ResourceSurrogateId <", sql);
        }

        [Fact]
        public void GivenNePrefix_WhenParse_ThenProducesNotEqualCondition()
        {
            var options = CreateOptions();
            _parser.Parse("_lastUpdated", "ne2024-01-15", options);
            var sql = options.SqlQueryBuilder.ToString();

            Assert.Contains("r.ResourceSurrogateId >=", sql);
            Assert.Contains("OR", sql);
        }

        [Fact]
        public void GivenEmptyValue_WhenParse_ThenThrows()
        {
            var options = CreateOptions();
            Assert.Throws<ArgumentNullException>(() => _parser.Parse("_lastUpdated", string.Empty, options));
        }

        [Fact]
        public void GivenParse_WhenCalled_ThenSelectsFromResource()
        {
            var options = CreateOptions();
            _parser.Parse("_lastUpdated", "2024-01-15", options);
            var sql = options.SqlQueryBuilder.ToString();

            Assert.Contains("FROM dbo.Resource", sql);
            Assert.Contains("cte0", sql);
        }

        [Fact]
        public void GivenParse_WhenCalled_ThenSetsResultCteName()
        {
            var options = CreateOptions();
            _parser.Parse("_lastUpdated", "2024-01-15", options);

            Assert.Equal("cte0", options.ResultCteName);
        }
    }
}

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
    public class IdSqlParserTests
    {
        private readonly IdSqlParser _parser = new IdSqlParser();

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
        public void GivenSingleId_WhenParse_ThenProducesEqualsCondition()
        {
            var options = CreateOptions();
            _parser.Parse("_id", "123", options);
            var sql = options.SqlQueryBuilder.ToString();

            Assert.Contains("r.ResourceId = '123'", sql);
            Assert.Contains("cte0", sql);
        }

        [Fact]
        public void GivenMultipleIds_WhenParse_ThenProducesInClause()
        {
            var options = CreateOptions();
            _parser.Parse("_id", "123,456,789", options);
            var sql = options.SqlQueryBuilder.ToString();

            Assert.Contains("r.ResourceId IN ('123', '456', '789')", sql);
        }

        [Fact]
        public void GivenNotModifier_WhenParse_ThenProducesNotEqualsCondition()
        {
            var options = CreateOptions();
            _parser.Parse("_id:not", "123", options);
            var sql = options.SqlQueryBuilder.ToString();

            Assert.Contains("r.ResourceId <> '123'", sql);
        }

        [Fact]
        public void GivenNotModifierWithMultipleIds_WhenParse_ThenProducesNotInClause()
        {
            var options = CreateOptions();
            _parser.Parse("_id:not", "123,456", options);
            var sql = options.SqlQueryBuilder.ToString();

            Assert.Contains("r.ResourceId NOT IN ('123', '456')", sql);
        }

        [Fact]
        public void GivenEmptyValue_WhenParse_ThenThrows()
        {
            var options = CreateOptions();
            Assert.Throws<ArgumentException>(() => _parser.Parse("_id", string.Empty, options));
        }

        [Fact]
        public void GivenIdWithSingleQuote_WhenParse_ThenEscapesValue()
        {
            var options = CreateOptions();
            _parser.Parse("_id", "ab'cd", options);
            var sql = options.SqlQueryBuilder.ToString();

            Assert.Contains("ab''cd", sql);
        }

        [Fact]
        public void GivenOptions_WhenParse_ThenSetsResultCteName()
        {
            var options = CreateOptions();
            _parser.Parse("_id", "123", options);

            Assert.Equal("cte0", options.ResultCteName);
        }

        [Fact]
        public void GivenChainLevel_WhenParse_ThenUsesChainCteName()
        {
            var options = CreateOptions();
            options.ChainLevel = 1;
            _parser.Parse("_id", "123", options);

            Assert.Equal("cte0chain1", options.ResultCteName);
        }

        [Fact]
        public void GivenParse_WhenCalled_ThenSelectsFromResource()
        {
            var options = CreateOptions();
            _parser.Parse("_id", "123", options);
            var sql = options.SqlQueryBuilder.ToString();

            Assert.Contains("FROM dbo.Resource", sql);
            Assert.Contains("SELECT DISTINCT", sql);
        }
    }
}

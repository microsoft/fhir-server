// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.SpecialParsers;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Features.Storage;
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
            return CreateOptions(new SqlCommand(), cteNumber);
        }

        private static ParserOptions CreateOptions(SqlCommand command, int cteNumber = 0)
        {
            return new ParserOptions
            {
                CteNumber = cteNumber,
                SqlQueryBuilder = new SqlQueryBuilder(),
                ResourceTypes = new List<short> { 1 },
                ParameterManager = new HashingSqlQueryParameterManager(
                    new SqlQueryParameterManager(command.Parameters)),
                ReuseQueryPlans = true,
            };
        }

        [Fact]
        public void GivenSingleId_WhenParse_ThenProducesEqualsCondition()
        {
            using var command = new SqlCommand();
            var options = CreateOptions(command);
            _parser.Parse("_id", "123", options);
            var sql = options.SqlQueryBuilder.ToString();

            Assert.Contains("r.ResourceId = @p0", sql, StringComparison.Ordinal);
            Assert.Contains("cte0", sql);
        }

        [Fact]
        public void GivenMultipleIds_WhenParse_ThenProducesInClause()
        {
            using var command = new SqlCommand();
            var options = CreateOptions(command);
            _parser.Parse("_id", "123,456,789", options);
            var sql = options.SqlQueryBuilder.ToString();

            Assert.Contains("r.ResourceId IN (@p0, @p1, @p2)", sql, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenNotModifier_WhenParse_ThenProducesNotEqualsCondition()
        {
            using var command = new SqlCommand();
            var options = CreateOptions(command);
            _parser.Parse("_id:not", "123", options);
            var sql = options.SqlQueryBuilder.ToString();

            Assert.Contains("r.ResourceId <> @p0", sql, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenNotModifierWithMultipleIds_WhenParse_ThenProducesNotInClause()
        {
            using var command = new SqlCommand();
            var options = CreateOptions(command);
            _parser.Parse("_id:not", "123,456", options);
            var sql = options.SqlQueryBuilder.ToString();

            Assert.Contains("r.ResourceId NOT IN (@p0, @p1)", sql, StringComparison.Ordinal);
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
            using var command = new SqlCommand();
            var options = CreateOptions(command);
            _parser.Parse("_id", "ab'cd", options);
            var sql = options.SqlQueryBuilder.ToString();

            Assert.DoesNotContain("ab'cd", sql, StringComparison.Ordinal);
            Assert.Equal("ab'cd", command.Parameters["@p0"].Value);
        }

        [Fact]
        public void GivenSingleId_WhenParse_ThenUsesTypedParameterExcludedFromHash()
        {
            using var command = new SqlCommand();
            var options = CreateOptions(command);

            _parser.Parse("_id", "123", options);
            var sql = options.SqlQueryBuilder.ToString();

            Assert.Contains("r.ResourceId = @p0", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("123", sql, StringComparison.Ordinal);
            Assert.Equal("123", command.Parameters["@p0"].Value);
            Assert.Equal(SqlDbType.VarChar, command.Parameters["@p0"].SqlDbType);
            Assert.False(options.ParameterManager!.HasParametersToHash);
        }

        [Fact]
        public void GivenMultipleIds_WhenParse_ThenUsesDistinctParametersExcludedFromHash()
        {
            using var command = new SqlCommand();
            var options = CreateOptions(command);

            _parser.Parse("_id", "123,456,789", options);
            var sql = options.SqlQueryBuilder.ToString();

            Assert.Contains("r.ResourceId IN (@p0, @p1, @p2)", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("'123'", sql, StringComparison.Ordinal);
            Assert.Collection(
                command.Parameters.Cast<SqlParameter>(),
                parameter =>
                {
                    Assert.Equal("@p0", parameter.ParameterName);
                    Assert.Equal("123", parameter.Value);
                    Assert.Equal(SqlDbType.VarChar, parameter.SqlDbType);
                },
                parameter =>
                {
                    Assert.Equal("@p1", parameter.ParameterName);
                    Assert.Equal("456", parameter.Value);
                    Assert.Equal(SqlDbType.VarChar, parameter.SqlDbType);
                },
                parameter =>
                {
                    Assert.Equal("@p2", parameter.ParameterName);
                    Assert.Equal("789", parameter.Value);
                    Assert.Equal(SqlDbType.VarChar, parameter.SqlDbType);
                });
            Assert.False(options.ParameterManager!.HasParametersToHash);
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

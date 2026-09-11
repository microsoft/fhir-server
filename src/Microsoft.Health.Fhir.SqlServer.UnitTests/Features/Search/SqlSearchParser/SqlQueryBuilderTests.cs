// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.SqlSearchParser
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SqlQueryBuilderTests
    {
        [Fact]
        public void GivenNewBuilder_WhenToString_ThenReturnsEmpty()
        {
            var builder = new SqlQueryBuilder();
            Assert.Equal(string.Empty, builder.ToString());
            Assert.Equal(0, builder.Length);
        }

        [Fact]
        public void GivenBuilder_WhenAppendLine_ThenAppendsTextWithNewline()
        {
            var builder = new SqlQueryBuilder();
            builder.AppendLine("hello");
            Assert.Contains("hello", builder.ToString());
            Assert.EndsWith(Environment.NewLine, builder.ToString());
        }

        [Fact]
        public void GivenBuilder_WhenSelectSingleColumn_ThenProducesSelectLine()
        {
            var builder = new SqlQueryBuilder();
            builder.Select("col1");
            Assert.Contains("SELECT col1", builder.ToString());
        }

        [Fact]
        public void GivenBuilder_WhenSelectMultipleColumns_ThenProducesCommaSeparated()
        {
            var builder = new SqlQueryBuilder();
            builder.Select("col1", "col2", "col3");
            var sql = builder.ToString();
            Assert.Contains("SELECT col1, col2, col3", sql);
        }

        [Fact]
        public void GivenBuilder_WhenSelectWithModifier_ThenIncludesModifier()
        {
            var builder = new SqlQueryBuilder();
            builder.SelectWithModifier("DISTINCT", "col1", "col2");
            var sql = builder.ToString();
            Assert.Contains("SELECT DISTINCT col1, col2", sql);
        }

        [Fact]
        public void GivenBuilder_WhenSelectWithTopModifier_ThenIncludesTop()
        {
            var builder = new SqlQueryBuilder();
            builder.SelectWithModifier("TOP 100", "col1");
            Assert.Contains("SELECT TOP 100 col1", builder.ToString());
        }

        [Fact]
        public void GivenBuilder_WhenFromWithAlias_ThenProducesFromAs()
        {
            var builder = new SqlQueryBuilder();
            builder.From("dbo.Resource", "r");
            Assert.Contains("FROM dbo.Resource AS r", builder.ToString());
        }

        [Fact]
        public void GivenBuilder_WhenFromWithoutAlias_ThenProducesFrom()
        {
            var builder = new SqlQueryBuilder();
            builder.From("dbo.Resource");
            Assert.Contains("FROM dbo.Resource", builder.ToString());
        }

        [Fact]
        public void GivenBuilder_WhenInnerJoin_ThenProducesInnerJoinOn()
        {
            var builder = new SqlQueryBuilder();
            builder.InnerJoin("dbo.TokenSearchParam", "t", "t.ResourceSurrogateId = r.ResourceSurrogateId");
            var sql = builder.ToString();
            Assert.Contains("INNER JOIN dbo.TokenSearchParam AS t ON t.ResourceSurrogateId = r.ResourceSurrogateId", sql);
        }

        [Fact]
        public void GivenBuilder_WhenLeftJoin_ThenProducesLeftJoinOn()
        {
            var builder = new SqlQueryBuilder();
            builder.LeftJoin("dbo.Resource", "r", "r.Id = t.Id");
            Assert.Contains("LEFT JOIN dbo.Resource AS r ON r.Id = t.Id", builder.ToString());
        }

        [Fact]
        public void GivenBuilder_WhenWhereAndOr_ThenProducesCorrectClauses()
        {
            var builder = new SqlQueryBuilder();
            builder.Where("col1 = 1");
            builder.And("col2 = 2");
            builder.Or("col3 = 3");
            var sql = builder.ToString();
            Assert.Contains("WHERE col1 = 1", sql);
            Assert.Contains("AND col2 = 2", sql);
            Assert.Contains("OR col3 = 3", sql);
        }

        [Fact]
        public void GivenBuilder_WhenOrderBy_ThenProducesOrderBy()
        {
            var builder = new SqlQueryBuilder();
            builder.OrderBy("col1 ASC");
            Assert.Contains("ORDER BY col1 ASC", builder.ToString());
        }

        [Fact]
        public void GivenBuilder_WhenBeginAndEndCte_ThenProducesWithAs()
        {
            var builder = new SqlQueryBuilder();
            builder.BeginCte("cte0");
            builder.Select("1");
            builder.EndCte();
            var sql = builder.ToString();
            Assert.Contains(";WITH", sql);
            Assert.Contains("cte0 AS (", sql);
            Assert.Contains(")", sql);
        }

        [Fact]
        public void GivenBuilder_WhenMultipleCtes_ThenSecondUsesComma()
        {
            var builder = new SqlQueryBuilder();
            builder.BeginCte("cte0");
            builder.Select("1");
            builder.EndCte();
            builder.AppendLine();
            builder.BeginCte("cte1");
            builder.Select("2");
            builder.EndCte();
            var sql = builder.ToString();
            Assert.Contains(";WITH", sql);
            Assert.Contains("cte0 AS (", sql);
            Assert.Contains(",", sql);
            Assert.Contains("cte1 AS (", sql);
        }

        [Fact]
        public void GivenBuilder_WhenEndCteWithoutBegin_ThenThrows()
        {
            var builder = new SqlQueryBuilder();
            Assert.Throws<InvalidOperationException>(() => builder.EndCte());
        }

        [Fact]
        public void GivenBuilder_WhenIncreaseAndDecreaseIndent_ThenIndentLevelChanges()
        {
            var builder = new SqlQueryBuilder();
            Assert.Equal(0, builder.IndentLevel);

            builder.IncreaseIndent();
            Assert.Equal(1, builder.IndentLevel);

            builder.IncreaseIndent(2);
            Assert.Equal(3, builder.IndentLevel);

            builder.DecreaseIndent(2);
            Assert.Equal(1, builder.IndentLevel);

            builder.DecreaseIndent(5);
            Assert.Equal(0, builder.IndentLevel);
        }

        [Fact]
        public void GivenBuilder_WhenClear_ThenResetsState()
        {
            var builder = new SqlQueryBuilder();
            builder.AppendLine("SELECT 1");
            builder.IncreaseIndent();
            builder.Clear();
            Assert.Equal(string.Empty, builder.ToString());
            Assert.Equal(0, builder.Length);
            Assert.Equal(0, builder.IndentLevel);
        }

        [Fact]
        public void GivenBuilder_WhenJoinMultiLine_ThenProducesMultiLineJoin()
        {
            var builder = new SqlQueryBuilder();
            builder.JoinMultiLine("INNER", "dbo.Table", "t", "t.Col1 = r.Col1", "t.Col2 = r.Col2");
            var sql = builder.ToString();
            Assert.Contains("INNER JOIN dbo.Table AS t", sql);
            Assert.Contains("ON t.Col1 = r.Col1", sql);
            Assert.Contains("AND t.Col2 = r.Col2", sql);
        }

        [Fact]
        public void GivenBuilder_WhenGroupBy_ThenProducesGroupBy()
        {
            var builder = new SqlQueryBuilder();
            builder.GroupBy("col1, col2");
            Assert.Contains("GROUP BY col1, col2", builder.ToString());
        }

        [Fact]
        public void GivenBuilder_WhenHaving_ThenProducesHaving()
        {
            var builder = new SqlQueryBuilder();
            builder.Having("COUNT(*) > 1");
            Assert.Contains("HAVING COUNT(*) > 1", builder.ToString());
        }

        [Fact]
        public void GivenBuilder_WhenAppend_ThenAppendsWithoutNewline()
        {
            var builder = new SqlQueryBuilder();
            builder.Append("hello ");
            builder.Append("world");
            var sql = builder.ToString();
            Assert.Contains("hello world", sql);
            Assert.DoesNotContain(Environment.NewLine + "world", sql);
        }

        [Fact]
        public void GivenBuilder_WhenSelectNoColumns_ThenProducesSelectOnly()
        {
            var builder = new SqlQueryBuilder();
            builder.Select();
            Assert.Contains("SELECT", builder.ToString());
        }
    }
}

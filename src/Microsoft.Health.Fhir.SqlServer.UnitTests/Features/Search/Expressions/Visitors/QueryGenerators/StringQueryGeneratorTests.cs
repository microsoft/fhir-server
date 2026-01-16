// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions.Visitors.QueryGenerators
{
    /// <summary>
    /// Unit tests for StringQueryGenerator.
    /// Tests the generator's ability to create SQL queries for string searches.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class StringQueryGeneratorTests
    {
        private readonly ISqlServerFhirModel _model;
        private readonly SchemaInformation _schemaInformation;

        public StringQueryGeneratorTests()
        {
            _model = Substitute.For<ISqlServerFhirModel>();
            _schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
            _schemaInformation.Current = SchemaVersionConstants.Max;
        }

        [Fact]
        public void GivenStringQueryGenerator_WhenInstanceAccessed_ThenNotNull()
        {
            Assert.NotNull(StringQueryGenerator.Instance);
        }

        [Fact]
        public void GivenStringQueryGenerator_WhenTableAccessed_ThenReturnsStringSearchParamTable()
        {
            var table = StringQueryGenerator.Instance.Table;

            Assert.Equal(VLatest.StringSearchParam.TableName, table.TableName);
        }

        [Theory]
        [InlineData(StringOperator.Equals, true, "test")]
        [InlineData(StringOperator.Equals, false, "Test")]
        [InlineData(StringOperator.Contains, true, "substring")]
        [InlineData(StringOperator.StartsWith, true, "start")]
        [InlineData(StringOperator.EndsWith, true, "end")]
        public void GivenStringExpression_WhenVisited_ThenGeneratesCorrectSqlQuery(StringOperator stringOperator, bool ignoreCase, string value)
        {
            var expression = new StringExpression(stringOperator, FieldName.String, null, value, ignoreCase);
            var context = CreateContext();

            StringQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.StringSearchParam.Text.Metadata.Name, sql);
            Assert.NotEmpty(sql);
            Assert.True(context.Parameters.HasParametersToHash);
        }

        [Fact]
        public void GivenStringEqualsExpression_WhenVisited_ThenUsesEqualsOperator()
        {
            var expression = new StringExpression(StringOperator.Equals, FieldName.String, null, "test", true);
            var context = CreateContext();

            StringQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains("=", sql);
            Assert.Contains(VLatest.StringSearchParam.Text.Metadata.Name, sql);
        }

        [Fact]
        public void GivenStringContainsExpression_WhenVisited_ThenUsesLikeOperator()
        {
            var expression = new StringExpression(StringOperator.Contains, FieldName.String, null, "substring", true);
            var context = CreateContext();

            StringQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains("LIKE", sql);
            Assert.Contains(VLatest.StringSearchParam.Text.Metadata.Name, sql);
        }

        [Fact]
        public void GivenStringStartsWithExpression_WhenVisited_ThenUsesLikeOperator()
        {
            var expression = new StringExpression(StringOperator.StartsWith, FieldName.String, null, "start", true);
            var context = CreateContext();

            StringQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains("LIKE", sql);
            Assert.Contains(VLatest.StringSearchParam.Text.Metadata.Name, sql);
        }

        [Fact]
        public void GivenStringEndsWithExpression_WhenVisited_ThenUsesLikeOperator()
        {
            var expression = new StringExpression(StringOperator.EndsWith, FieldName.String, null, "end", true);
            var context = CreateContext();

            StringQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains("LIKE", sql);
            Assert.Contains(VLatest.StringSearchParam.Text.Metadata.Name, sql);
        }

        [Fact]
        public void GivenCaseSensitiveStringExpression_WhenVisited_ThenGeneratesCorrectCollation()
        {
            var expression = new StringExpression(StringOperator.Equals, FieldName.String, null, "test", false);
            var context = CreateContext();

            StringQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains("COLLATE", sql);
            Assert.Contains("CS_AS", sql);
        }

        [Fact]
        public void GivenTextOverflowExpression_WhenVisited_ThenChecksForNotNull()
        {
            var expression = new StringExpression(StringOperator.Equals, SqlFieldName.TextOverflow, null, "overflow", true);
            var context = CreateContext();

            StringQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.StringSearchParam.TextOverflow.Metadata.Name, sql);
            Assert.Contains("IS NOT NULL", sql);
            Assert.Contains("AND", sql);
        }

        [Fact]
        public void GivenStringExpressionWithComponentIndex_WhenVisited_ThenIncludesComponentIndex()
        {
            var expression = new StringExpression(StringOperator.Equals, FieldName.String, 0, "test", true);
            var context = CreateContext();

            StringQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.StringSearchParam.Text.Metadata.Name + "1", sql);
        }

        [Fact]
        public void GivenStringExpressionWithTableAlias_WhenVisited_ThenSqlContainsTableAlias()
        {
            const string tableAlias = "str";
            var expression = new StringExpression(StringOperator.Equals, FieldName.String, null, "test", true);
            var context = CreateContext(tableAlias);

            StringQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains($"{tableAlias}.{VLatest.StringSearchParam.Text.Metadata.Name}", sql);
        }

        [Theory]
        [InlineData(StringOperator.NotContains, "NOT")]
        [InlineData(StringOperator.NotStartsWith, "NOT")]
        [InlineData(StringOperator.NotEndsWith, "NOT")]
        public void GivenNegativeStringExpression_WhenVisited_ThenIncludesNotOperator(StringOperator stringOperator, string expectedOperator)
        {
            var expression = new StringExpression(stringOperator, FieldName.String, null, "test", true);
            var context = CreateContext();

            StringQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(expectedOperator, sql);
            Assert.Contains("LIKE", sql);
        }

        [Fact]
        public void GivenStringWithSpecialCharacters_WhenVisited_ThenEscapesCorrectly()
        {
            var expression = new StringExpression(StringOperator.Contains, FieldName.String, null, "test%value", true);
            var context = CreateContext();

            StringQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains("ESCAPE", sql);
            Assert.Contains("!", sql);
        }

        [Fact]
        public void GivenMultipleStringExpressions_WhenVisited_ThenEachGeneratesSQL()
        {
            var expression1 = new StringExpression(StringOperator.Equals, FieldName.String, null, "test1", true);
            var expression2 = new StringExpression(StringOperator.Contains, FieldName.String, null, "test2", true);

            var context1 = CreateContext();
            var context2 = CreateContext();

            StringQueryGenerator.Instance.VisitString(expression1, context1);
            StringQueryGenerator.Instance.VisitString(expression2, context2);

            var sql1 = context1.StringBuilder.ToString();
            var sql2 = context2.StringBuilder.ToString();

            Assert.Contains(VLatest.StringSearchParam.Text.Metadata.Name, sql1);
            Assert.Contains("=", sql1);

            Assert.Contains(VLatest.StringSearchParam.Text.Metadata.Name, sql2);
            Assert.Contains("LIKE", sql2);

            Assert.True(context1.Parameters.HasParametersToHash);
            Assert.True(context2.Parameters.HasParametersToHash);
        }

        [Fact]
        public void GivenInvalidFieldName_WhenVisited_ThenThrowsArgumentOutOfRangeException()
        {
            var expression = new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "test", true);
            var context = CreateContext();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                StringQueryGenerator.Instance.VisitString(expression, context));
        }

        [Theory]
        [InlineData("")]
        [InlineData("a")]
        [InlineData("test")]
        [InlineData("a very long string value for testing")]
        [InlineData("unicode: סיאü")]
        public void GivenVariousStringValues_WhenVisited_ThenGeneratesSQL(string value)
        {
            var expression = new StringExpression(StringOperator.Equals, FieldName.String, null, value, true);
            var context = CreateContext();

            StringQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.NotEmpty(sql);
            Assert.Contains(VLatest.StringSearchParam.Text.Metadata.Name, sql);
            Assert.True(context.Parameters.HasParametersToHash);
        }

        [Fact]
        public void GivenLeftSideStartsWithExpression_WhenVisited_ThenGeneratesReversedLikeQuery()
        {
            var expression = new StringExpression(StringOperator.LeftSideStartsWith, FieldName.String, null, "test", true);
            var context = CreateContext();

            StringQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains("LIKE", sql);
            Assert.Contains("+'%'", sql);
        }

        [Fact]
        public void GivenCaseSensitiveEqualsExpression_WhenVisited_ThenIncludesAdditionalInsensitivePredicate()
        {
            var expression = new StringExpression(StringOperator.Equals, FieldName.String, null, "Test", false);
            var context = CreateContext();

            StringQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains("=", sql);
            Assert.Contains("AND", sql);
            Assert.Contains("COLLATE", sql);
        }

        private SearchParameterQueryGeneratorContext CreateContext(string tableAlias = null)
        {
            var stringBuilder = new IndentedStringBuilder(new StringBuilder());
            var sqlCommand = new SqlCommand();
            var sqlParameterManager = new SqlQueryParameterManager(sqlCommand.Parameters);
            var parameters = new HashingSqlQueryParameterManager(sqlParameterManager);

            return new SearchParameterQueryGeneratorContext(
                stringBuilder,
                parameters,
                _model,
                _schemaInformation,
                isAsyncOperation: false,
                tableAlias);
        }
    }
}

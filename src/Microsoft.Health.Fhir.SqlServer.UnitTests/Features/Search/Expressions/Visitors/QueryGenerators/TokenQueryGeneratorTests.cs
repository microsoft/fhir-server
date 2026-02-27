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
    /// Unit tests for TokenQueryGenerator.
    /// Tests the generator's ability to create SQL queries for token searches.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class TokenQueryGeneratorTests
    {
        private readonly ISqlServerFhirModel _model;
        private readonly SchemaInformation _schemaInformation;

        public TokenQueryGeneratorTests()
        {
            _model = Substitute.For<ISqlServerFhirModel>();
            _schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
            _schemaInformation.Current = SchemaVersionConstants.Max;
        }

        [Fact]
        public void GivenTokenQueryGenerator_WhenInstanceAccessed_ThenNotNull()
        {
            Assert.NotNull(TokenQueryGenerator.Instance);
        }

        [Fact]
        public void GivenTokenQueryGenerator_WhenTableAccessed_ThenReturnsTokenSearchParamTable()
        {
            var table = TokenQueryGenerator.Instance.Table;

            Assert.Equal(VLatest.TokenSearchParam.TableName, table.TableName);
        }

        [Fact]
        public void GivenTokenCodeExpression_WhenVisited_ThenGeneratesCorrectSqlQuery()
        {
            var expression = new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "code123", true);
            var context = CreateContext();

            TokenQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.TokenSearchParam.Code.Metadata.Name, sql);
            Assert.NotEmpty(sql);
            Assert.True(context.Parameters.HasParametersToHash);
        }

        [Fact]
        public void GivenTokenSystemExpression_WhenSystemIdExists_ThenUsesDirectComparison()
        {
            const string systemValue = "http://loinc.org";
            const int systemId = 1;

            _model.TryGetSystemId(systemValue, out Arg.Any<int>())
                .Returns(x =>
                {
                    x[1] = systemId;
                    return true;
                });

            var expression = new StringExpression(StringOperator.Equals, FieldName.TokenSystem, null, systemValue, true);
            var context = CreateContext();

            TokenQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Matches(@"SystemId\s*=\s*@?\w+", sql);
        }

        [Fact]
        public void GivenTokenSystemExpression_WhenSystemIdNotExists_ThenUsesSubquery()
        {
            const string systemValue = "http://custom-system.org";

            _model.TryGetSystemId(systemValue, out Arg.Any<int>())
                .Returns(false);

            var expression = new StringExpression(StringOperator.Equals, FieldName.TokenSystem, null, systemValue, true);
            var context = CreateContext();

            TokenQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Matches(@"SystemId\s+IN\s*\(\s*SELECT", sql);
            Assert.Contains(VLatest.System.TableName, sql);
        }

        [Fact]
        public void GivenShortTokenCode_WhenVisited_ThenOnlyUsesCodeColumn()
        {
            var shortCode = "ABC";
            var expression = new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, shortCode, true);
            var context = CreateContext();

            TokenQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.TokenSearchParam.Code.Metadata.Name, sql);
            Assert.DoesNotContain(VLatest.TokenSearchParam.CodeOverflow.Metadata.Name, sql);
        }

        [Fact]
        public void GivenTokenCodeAtMaxLength_WhenVisited_ThenChecksCodeOverflowIsNull()
        {
            var maxLengthCode = new string('A', (int)VLatest.TokenSearchParam.Code.Metadata.MaxLength);
            var expression = new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, maxLengthCode, true);
            var context = CreateContext();

            TokenQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.TokenSearchParam.Code.Metadata.Name, sql);
            Assert.Contains(VLatest.TokenSearchParam.CodeOverflow.Metadata.Name, sql);
            Assert.Contains("IS NULL", sql);
        }

        [Fact]
        public void GivenLongTokenCode_WhenVisited_ThenUsesCodeAndCodeOverflow()
        {
            var longCode = new string('A', (int)VLatest.TokenSearchParam.Code.Metadata.MaxLength + 50);
            var expression = new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, longCode, true);
            var context = CreateContext();

            TokenQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.TokenSearchParam.Code.Metadata.Name, sql);
            Assert.Contains(VLatest.TokenSearchParam.CodeOverflow.Metadata.Name, sql);
            Assert.Contains("IS NOT NULL", sql);
        }

        [Fact]
        public void GivenVeryLongTokenCode_WhenVisited_ThenIncludesTruncation128LogicIsNotTriggered()
        {
            var veryLongCode = new string('A', 150);
            var expression = new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, veryLongCode, true);
            var context = CreateContext();

            TokenQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            // Verify truncation-128 logic does not exist
            // - Should not have nested structure with OR for handling truncated codes
            // - Code = @... for the full value match
            // - No OR branch with Code = @... for the 128-truncated value match
            Assert.DoesNotContain("((", sql);
            Assert.DoesNotContain("OR", sql);
            Assert.Matches(@"Code\s*=\s*@\w+", sql);

            // Verify the SQL contains the OR branch for 128-truncation
            // The structure should be ((Code = @p0 ...) OR (Code = @p1 ...))
            Assert.DoesNotContain("))", sql);
        }

        [Fact]
        public void GivenTokenExpressionWithComponentIndex_WhenVisited_ThenIncludesComponentIndex()
        {
            var expression = new StringExpression(StringOperator.Equals, FieldName.TokenCode, 0, "code123", true);
            var context = CreateContext();

            TokenQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.TokenSearchParam.Code.Metadata.Name + "1", sql);
        }

        [Fact]
        public void GivenTokenExpressionWithTableAlias_WhenVisited_ThenSqlContainsTableAlias()
        {
            const string tableAlias = "tok";
            var expression = new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "code123", true);
            var context = CreateContext(tableAlias);

            TokenQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains($"{tableAlias}.{VLatest.TokenSearchParam.Code.Metadata.Name}", sql);
        }

        [Fact]
        public void GivenMissingFieldExpression_WhenVisited_ThenChecksSystemIdIsNull()
        {
            var expression = new MissingFieldExpression(FieldName.TokenSystem, null);
            var context = CreateContext();

            TokenQueryGenerator.Instance.VisitMissingField(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.TokenSearchParam.SystemId.Metadata.Name, sql);
            Assert.Contains("IS NULL", sql);
        }

        [Fact]
        public void GivenInvalidFieldName_WhenVisited_ThenThrowsInvalidOperationException()
        {
            var expression = new StringExpression(StringOperator.Equals, FieldName.DateTimeStart, null, "invalid", true);
            var context = CreateContext();

            Assert.Throws<InvalidOperationException>(() =>
                TokenQueryGenerator.Instance.VisitString(expression, context));
        }

        [Fact]
        public void GivenMultipleTokenExpressions_WhenVisited_ThenEachGeneratesSQL()
        {
            var expression1 = new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "code1", true);
            var expression2 = new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "code2", true);

            var context1 = CreateContext();
            var context2 = CreateContext();

            TokenQueryGenerator.Instance.VisitString(expression1, context1);
            TokenQueryGenerator.Instance.VisitString(expression2, context2);

            var sql1 = context1.StringBuilder.ToString();
            var sql2 = context2.StringBuilder.ToString();

            Assert.Contains(VLatest.TokenSearchParam.Code.Metadata.Name, sql1);
            Assert.Contains(VLatest.TokenSearchParam.Code.Metadata.Name, sql2);

            Assert.True(context1.Parameters.HasParametersToHash);
            Assert.True(context2.Parameters.HasParametersToHash);
        }

        [Theory]
        [InlineData("http://loinc.org")]
        [InlineData("http://snomed.info/sct")]
        [InlineData("http://hl7.org/fhir/sid/us-ssn")]
        [InlineData("urn:oid:1.2.3.4.5")]
        public void GivenVariousSystemValues_WhenVisited_ThenGeneratesSQL(string systemValue)
        {
            _model.TryGetSystemId(systemValue, out Arg.Any<int>())
                .Returns(x =>
                {
                    x[1] = 1;
                    return true;
                });

            var expression = new StringExpression(StringOperator.Equals, FieldName.TokenSystem, null, systemValue, true);
            var context = CreateContext();

            TokenQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.NotEmpty(sql);
            Assert.Contains(VLatest.TokenSearchParam.SystemId.Metadata.Name, sql);
        }

        [Theory]
        [InlineData("M")]
        [InlineData("F")]
        [InlineData("active")]
        [InlineData("12345")]
        [InlineData("code-with-dashes")]
        public void GivenVariousCodeValues_WhenVisited_ThenGeneratesSQL(string codeValue)
        {
            var expression = new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, codeValue, true);
            var context = CreateContext();

            TokenQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.NotEmpty(sql);
            Assert.Contains(VLatest.TokenSearchParam.Code.Metadata.Name, sql);
            Assert.True(context.Parameters.HasParametersToHash);
        }

        [Fact]
        public void GivenTokenCodeWithSpecialCharacters_WhenVisited_ThenHandlesCorrectly()
        {
            const string codeWithSpecialChars = "code_with-special.chars";
            var expression = new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, codeWithSpecialChars, true);
            var context = CreateContext();

            TokenQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.NotEmpty(sql);
            Assert.Contains(VLatest.TokenSearchParam.Code.Metadata.Name, sql);
        }

        [Fact]
        public void GivenTokenCodeExactly256Characters_WhenVisited_ThenHandlesMaxLengthCorrectly()
        {
            var code256 = new string('X', 256);
            var expression = new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, code256, true);
            var context = CreateContext();

            TokenQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.TokenSearchParam.Code.Metadata.Name, sql);
            Assert.Contains(VLatest.TokenSearchParam.CodeOverflow.Metadata.Name, sql);
        }

        [Fact]
        public void GivenTokenSystemWithComponentIndex_WhenVisited_ThenIncludesComponentIndex()
        {
            const string systemValue = "http://example.org";
            _model.TryGetSystemId(systemValue, out Arg.Any<int>())
                .Returns(x =>
                {
                    x[1] = 1;
                    return true;
                });

            var expression = new StringExpression(StringOperator.Equals, FieldName.TokenSystem, 0, systemValue, true);
            var context = CreateContext();

            TokenQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.TokenSearchParam.SystemId.Metadata.Name + "1", sql);
        }

        private SearchParameterQueryGeneratorContext CreateContext(string tableAlias = null)
        {
            var stringBuilder = new IndentedStringBuilder(new StringBuilder());
            using var sqlCommand = new SqlCommand();
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

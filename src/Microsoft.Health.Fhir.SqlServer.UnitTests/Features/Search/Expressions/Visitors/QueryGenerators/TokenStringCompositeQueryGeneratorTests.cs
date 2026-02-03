// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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
    /// Unit tests for TokenStringCompositeQueryGenerator.
    /// Tests the generator's ability to delegate to component generators (TokenQueryGenerator for component 0,
    /// StringQueryGenerator for component 1) and return the correct table reference.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class TokenStringCompositeQueryGeneratorTests
    {
        private readonly ISqlServerFhirModel _model;
        private readonly SchemaInformation _schemaInformation;

        public TokenStringCompositeQueryGeneratorTests()
        {
            _model = Substitute.For<ISqlServerFhirModel>();
            _schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
            _schemaInformation.Current = SchemaVersionConstants.Max;
        }

        [Fact]
        public void GivenTokenStringCompositeQueryGenerator_WhenTableAccessed_ThenReturnsTokenStringCompositeSearchParamTable()
        {
            var table = TokenStringCompositeQueryGenerator.Instance.Table;

            Assert.NotNull(table);
            Assert.Equal(VLatest.TokenStringCompositeSearchParam.TableName, table.TableName);
        }

        [Fact]
        public void GivenStringExpressionForTokenCodeWithComponentIndex0_WhenVisitString_ThenDelegatesToTokenQueryGenerator()
        {
            // Arrange - Component index 0 should delegate to TokenQueryGenerator
            var expression = new StringExpression(
                StringOperator.Equals,
                FieldName.TokenCode,
                componentIndex: 0,
                value: "active",
                ignoreCase: false);

            var context = CreateContext();

            // Act
            TokenStringCompositeQueryGenerator.Instance.VisitString(expression, context);

            // Assert - TokenQueryGenerator should generate SQL for token code with component 1 column suffix
            var sql = context.StringBuilder.ToString();
            Assert.NotEmpty(sql);
            Assert.Matches(@"Code1\s*=\s*@\w+", sql);
        }

        [Fact]
        public void GivenStringExpressionWithComponentIndex1_WhenVisitString_ThenDelegatesToStringQueryGenerator()
        {
            // Arrange - Component index 1 should delegate to StringQueryGenerator
            var expression = new StringExpression(
                StringOperator.Equals,
                FieldName.String,
                componentIndex: 1,
                value: "test-value",
                ignoreCase: false);

            var context = CreateContext();

            // Act
            TokenStringCompositeQueryGenerator.Instance.VisitString(expression, context);

            // Assert - StringQueryGenerator should generate SQL for string on component 2
            var sql = context.StringBuilder.ToString();
            Assert.NotEmpty(sql);
            Assert.Matches(@"Text2\s*=\s*@\w+", sql);
        }

        [Fact]
        public void GivenMissingFieldExpressionWithComponentIndex0_WhenVisitMissingField_ThenDelegatesToTokenQueryGenerator()
        {
            // Arrange - Component 0 should delegate to TokenQueryGenerator for missing field
            var expression = new MissingFieldExpression(FieldName.TokenSystem, componentIndex: 0);
            var context = CreateContext();

            // Act
            TokenStringCompositeQueryGenerator.Instance.VisitMissingField(expression, context);

            // Assert - Should check for NULL token system on component 1
            var sql = context.StringBuilder.ToString();
            Assert.NotEmpty(sql);
            Assert.Contains("SystemId1 IS NULL", sql);
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

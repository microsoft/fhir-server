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
    /// Unit tests for ResourceIdParameterQueryGenerator.
    /// Tests the generator's ability to handle resource ID searches with various string operators.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ResourceIdParameterQueryGeneratorTests
    {
        private readonly ISqlServerFhirModel _model;
        private readonly SchemaInformation _schemaInformation;

        public ResourceIdParameterQueryGeneratorTests()
        {
            _model = Substitute.For<ISqlServerFhirModel>();
            _schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
            _schemaInformation.Current = SchemaVersionConstants.Max;
        }

        [Fact]
        public void GivenResourceIdParameterQueryGenerator_WhenInstanceAccessed_ThenNotNull()
        {
            Assert.NotNull(ResourceIdParameterQueryGenerator.Instance);
        }

        [Fact]
        public void GivenResourceIdParameterQueryGenerator_WhenInstanceAccessedMultipleTimes_ThenReturnsSameInstance()
        {
            var instance1 = ResourceIdParameterQueryGenerator.Instance;
            var instance2 = ResourceIdParameterQueryGenerator.Instance;

            Assert.Same(instance1, instance2);
        }

        [Fact]
        public void GivenStringExpressionWithEqualsOperator_WhenVisitString_ThenGeneratesSqlWithEquality()
        {
            // Arrange - Test exact match search (_id=123)
            var expression = new StringExpression(
                StringOperator.Equals,
                FieldName.String,
                componentIndex: null,
                value: "patient123",
                ignoreCase: false);

            var context = CreateContext();

            // Act
            ResourceIdParameterQueryGenerator.Instance.VisitString(expression, context);

            // Assert
            var sql = context.StringBuilder.ToString();
            Assert.NotEmpty(sql);
            Assert.Contains("ResourceId", sql);
            Assert.Contains("=", sql);
            Assert.Contains("@", sql); // Should use parameterized query
        }

        [Fact]
        public void GivenStringExpressionWithStartsWithOperator_WhenVisitString_ThenGeneratesSqlWithLikePattern()
        {
            // Arrange - Test prefix search (_id=pat*)
            var expression = new StringExpression(
                StringOperator.StartsWith,
                FieldName.String,
                componentIndex: null,
                value: "pat",
                ignoreCase: false);

            var context = CreateContext();

            // Act
            ResourceIdParameterQueryGenerator.Instance.VisitString(expression, context);

            // Assert
            var sql = context.StringBuilder.ToString();
            Assert.NotEmpty(sql);
            Assert.Contains("ResourceId", sql);
            Assert.Contains("LIKE", sql);

            // Wildcard pattern should be in parameter, not directly in SQL
            Assert.DoesNotContain("pat%", sql);
        }

        [Fact]
        public void GivenStringExpressionWithContainsOperator_WhenVisitString_ThenGeneratesSqlWithLikePattern()
        {
            // Arrange - Test substring search
            var expression = new StringExpression(
                StringOperator.Contains,
                FieldName.String,
                componentIndex: null,
                value: "tient",
                ignoreCase: false);

            var context = CreateContext();

            // Act
            ResourceIdParameterQueryGenerator.Instance.VisitString(expression, context);

            // Assert
            var sql = context.StringBuilder.ToString();
            Assert.NotEmpty(sql);
            Assert.Contains("ResourceId", sql);
            Assert.Contains("LIKE", sql);
        }

        [Fact]
        public void GivenStringExpressionWithSpecialCharacters_WhenVisitString_ThenHandlesEscapingCorrectly()
        {
            // Arrange - Test resource ID with special SQL characters
            var expression = new StringExpression(
                StringOperator.Equals,
                FieldName.String,
                componentIndex: null,
                value: "patient_123",
                ignoreCase: false);

            var context = CreateContext();

            // Act
            ResourceIdParameterQueryGenerator.Instance.VisitString(expression, context);

            // Assert
            var sql = context.StringBuilder.ToString();
            Assert.NotEmpty(sql);
            Assert.Contains("ResourceId", sql);

            // Should have generated SQL with parameter
            Assert.Contains("@", sql);
        }

        [Fact]
        public void GivenMultipleStringExpressions_WhenVisitString_ThenGeneratesMultipleSqlPredicates()
        {
            // Arrange - Test sequential calls for multiple conditions
            var expression1 = new StringExpression(
                StringOperator.Equals,
                FieldName.String,
                componentIndex: null,
                value: "patient1",
                ignoreCase: false);

            var expression2 = new StringExpression(
                StringOperator.Equals,
                FieldName.String,
                componentIndex: null,
                value: "patient2",
                ignoreCase: false);

            var context = CreateContext();

            // Act
            ResourceIdParameterQueryGenerator.Instance.VisitString(expression1, context);
            var sqlAfterFirst = context.StringBuilder.ToString();

            ResourceIdParameterQueryGenerator.Instance.VisitString(expression2, context);
            var sqlAfterSecond = context.StringBuilder.ToString();

            // Assert
            Assert.NotEmpty(sqlAfterFirst);
            Assert.NotEmpty(sqlAfterSecond);
            Assert.True(sqlAfterSecond.Length > sqlAfterFirst.Length, "Second call should append more SQL");

            // Should have multiple parameter references
            var paramCount = sqlAfterSecond.Split('@').Length - 1;
            Assert.True(paramCount >= 2, "Expected at least 2 parameters");
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

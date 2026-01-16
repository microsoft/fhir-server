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
    /// Unit tests for ResourceSurrogateIdParameterQueryGenerator.
    /// Tests the generator's ability to handle surrogate ID searches with conditional parameter hashing.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ResourceSurrogateIdParameterQueryGeneratorTests
    {
        private readonly ISqlServerFhirModel _model;
        private readonly SchemaInformation _schemaInformation;

        public ResourceSurrogateIdParameterQueryGeneratorTests()
        {
            _model = Substitute.For<ISqlServerFhirModel>();
            _schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
            _schemaInformation.Current = SchemaVersionConstants.Max;
        }

        [Fact]
        public void GivenResourceSurrogateIdParameterQueryGenerator_WhenInstanceAccessed_ThenNotNull()
        {
            Assert.NotNull(ResourceSurrogateIdParameterQueryGenerator.Instance);
        }

        [Fact]
        public void GivenResourceSurrogateIdParameterQueryGenerator_WhenInstanceAccessedMultipleTimes_ThenReturnsSameInstance()
        {
            var instance1 = ResourceSurrogateIdParameterQueryGenerator.Instance;
            var instance2 = ResourceSurrogateIdParameterQueryGenerator.Instance;

            Assert.Same(instance1, instance2);
        }

        [Fact]
        public void GivenBinaryExpressionWithEqualOperator_WhenVisitBinary_ThenGeneratesSqlWithEquality()
        {
            // Arrange - Test exact match (_resourceSurrogateId=12345)
            var expression = new BinaryExpression(
                BinaryOperator.Equal,
                FieldName.Number,
                componentIndex: null,
                value: 12345L);

            var context = CreateContext(isAsyncOperation: false);

            // Act
            ResourceSurrogateIdParameterQueryGenerator.Instance.VisitBinary(expression, context);

            // Assert
            var sql = context.StringBuilder.ToString();
            Assert.NotEmpty(sql);
            Assert.Contains("ResourceSurrogateId", sql);
            Assert.Contains("=", sql);
            Assert.Contains("@", sql); // Should use parameterized query
        }

        [Fact]
        public void GivenBinaryExpressionWithGreaterThanOperator_WhenVisitBinary_ThenGeneratesSqlWithGreaterThan()
        {
            // Arrange - Test range query (_resourceSurrogateId=gt12345)
            var expression = new BinaryExpression(
                BinaryOperator.GreaterThan,
                FieldName.Number,
                componentIndex: null,
                value: 12345L);

            var context = CreateContext(isAsyncOperation: false);

            // Act
            ResourceSurrogateIdParameterQueryGenerator.Instance.VisitBinary(expression, context);

            // Assert
            var sql = context.StringBuilder.ToString();
            Assert.NotEmpty(sql);
            Assert.Contains("ResourceSurrogateId", sql);
            Assert.Contains(">", sql);
            Assert.DoesNotContain(">=", sql);
        }

        [Fact]
        public void GivenBinaryExpressionWithLessThanOperator_WhenVisitBinary_ThenGeneratesSqlWithLessThan()
        {
            // Arrange - Test range query (_resourceSurrogateId=lt12345)
            var expression = new BinaryExpression(
                BinaryOperator.LessThan,
                FieldName.Number,
                componentIndex: null,
                value: 12345L);

            var context = CreateContext(isAsyncOperation: false);

            // Act
            ResourceSurrogateIdParameterQueryGenerator.Instance.VisitBinary(expression, context);

            // Assert
            var sql = context.StringBuilder.ToString();
            Assert.NotEmpty(sql);
            Assert.Contains("ResourceSurrogateId", sql);
            Assert.Contains("<", sql);
            Assert.DoesNotContain("<=", sql);
        }

        [Fact]
        public void GivenSyncOperation_WhenVisitBinary_ThenParameterNotIncludedInHash()
        {
            // Arrange - For synchronous operations, surrogate ID should NOT be hashed
            var expression = new BinaryExpression(
                BinaryOperator.Equal,
                FieldName.Number,
                componentIndex: null,
                value: 12345L);

            var context = CreateContext(isAsyncOperation: false);
            var initialHashCount = context.Parameters.ParametersToHash.Count;

            // Act
            ResourceSurrogateIdParameterQueryGenerator.Instance.VisitBinary(expression, context);

            // Assert - Parameter should not be added to hash for sync operations
            Assert.Equal(initialHashCount, context.Parameters.ParametersToHash.Count);
        }

        [Fact]
        public void GivenAsyncOperation_WhenVisitBinary_ThenParameterIncludedInHash()
        {
            // Arrange - For async operations, surrogate ID SHOULD be hashed for query plan reuse
            var expression = new BinaryExpression(
                BinaryOperator.Equal,
                FieldName.Number,
                componentIndex: null,
                value: 12345L);

            var context = CreateContext(isAsyncOperation: true);
            var initialHashCount = context.Parameters.ParametersToHash.Count;

            // Act
            ResourceSurrogateIdParameterQueryGenerator.Instance.VisitBinary(expression, context);

            // Assert - Parameter SHOULD be added to hash for async operations
            Assert.True(
                context.Parameters.ParametersToHash.Count > initialHashCount,
                "Async operations should include surrogate ID in parameter hash");
        }

        [Fact]
        public void GivenMultipleBinaryExpressions_WhenVisitBinary_ThenGeneratesMultipleSqlPredicates()
        {
            // Arrange - Test sequential calls for range queries
            var expression1 = new BinaryExpression(
                BinaryOperator.GreaterThanOrEqual,
                FieldName.Number,
                componentIndex: null,
                value: 1000L);

            var expression2 = new BinaryExpression(
                BinaryOperator.LessThan,
                FieldName.Number,
                componentIndex: null,
                value: 2000L);

            var context = CreateContext(isAsyncOperation: false);

            // Act
            ResourceSurrogateIdParameterQueryGenerator.Instance.VisitBinary(expression1, context);
            var sqlAfterFirst = context.StringBuilder.ToString();

            ResourceSurrogateIdParameterQueryGenerator.Instance.VisitBinary(expression2, context);
            var sqlAfterSecond = context.StringBuilder.ToString();

            // Assert
            Assert.NotEmpty(sqlAfterFirst);
            Assert.NotEmpty(sqlAfterSecond);
            Assert.True(sqlAfterSecond.Length > sqlAfterFirst.Length, "Second call should append more SQL");

            // Should have >= and <
            Assert.Contains(">=", sqlAfterSecond);
            Assert.Contains("<", sqlAfterSecond);
        }

        [Fact]
        public void GivenDifferentOperators_WhenVisitBinary_ThenGeneratesCorrectSqlOperators()
        {
            // Arrange - Test all comparison operators
            var testCases = new[]
            {
                new { Operator = BinaryOperator.Equal, Expected = " = " },
                new { Operator = BinaryOperator.GreaterThan, Expected = " > " },
                new { Operator = BinaryOperator.GreaterThanOrEqual, Expected = " >= " },
                new { Operator = BinaryOperator.LessThan, Expected = " < " },
                new { Operator = BinaryOperator.LessThanOrEqual, Expected = " <= " },
                new { Operator = BinaryOperator.NotEqual, Expected = " <> " },
            };

            foreach (var testCase in testCases)
            {
                // Arrange
                var expression = new BinaryExpression(
                    testCase.Operator,
                    FieldName.Number,
                    componentIndex: null,
                    value: 12345L);

                var context = CreateContext(isAsyncOperation: false);

                // Act
                ResourceSurrogateIdParameterQueryGenerator.Instance.VisitBinary(expression, context);

                // Assert
                var sql = context.StringBuilder.ToString();
                Assert.Contains(testCase.Expected, sql);
            }
        }

        private SearchParameterQueryGeneratorContext CreateContext(bool isAsyncOperation, string tableAlias = null)
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
                isAsyncOperation,
                tableAlias);
        }
    }
}

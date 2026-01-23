// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections;
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
    /// Unit tests for PrimaryKeyRangeParameterQueryGenerator.
    /// Tests the generator's ability to create SQL predicates for primary key range queries.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class PrimaryKeyRangeParameterQueryGeneratorTests
    {
        private readonly ISqlServerFhirModel _model;
        private readonly SchemaInformation _schemaInformation;

        public PrimaryKeyRangeParameterQueryGeneratorTests()
        {
            _model = Substitute.For<ISqlServerFhirModel>();
            _schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
            _schemaInformation.Current = SchemaVersionConstants.Max;
        }

        [Fact]
        public void GivenPrimaryKeyRangeParameterQueryGenerator_WhenInstanceAccessed_ThenNotNull()
        {
            Assert.NotNull(PrimaryKeyRangeParameterQueryGenerator.Instance);
        }

        [Fact]
        public void GivenPrimaryKeyRangeParameterQueryGenerator_WhenInstanceAccessedMultipleTimes_ThenReturnsSameInstance()
        {
            var instance1 = PrimaryKeyRangeParameterQueryGenerator.Instance;
            var instance2 = PrimaryKeyRangeParameterQueryGenerator.Instance;

            Assert.Same(instance1, instance2);
        }

        [Fact]
        public void GivenPrimaryKeyRangeParameterQueryGenerator_WhenTypeChecked_ThenIsResourceTableSearchParameterQueryGenerator()
        {
            var instance = PrimaryKeyRangeParameterQueryGenerator.Instance;

            Assert.IsAssignableFrom<ResourceTableSearchParameterQueryGenerator>(instance);
        }

        [Fact]
        public void GivenSimplePrimaryKeyRange_WhenVisitBinary_ThenGeneratesCorrectSql()
        {
            // Arrange
            var currentValue = new PrimaryKeyValue(1, 100);
            var nextResourceTypeIds = new BitArray(10);
            nextResourceTypeIds[2] = true;
            nextResourceTypeIds[3] = true;

            var primaryKeyRange = new PrimaryKeyRange(currentValue, nextResourceTypeIds);
            var expression = new BinaryExpression(BinaryOperator.GreaterThan, SqlFieldName.PrimaryKey, null, primaryKeyRange);
            var context = CreateContext();

            // Act
            PrimaryKeyRangeParameterQueryGenerator.Instance.VisitBinary(expression, context);

            // Assert
            var sql = context.StringBuilder.ToString();
            Assert.Contains("ResourceTypeId", sql);
            Assert.Contains("ResourceSurrogateId", sql);
            Assert.Contains("=", sql);
            Assert.Contains(">", sql);
            Assert.Contains("OR", sql);
            Assert.Contains("IN", sql);
        }

        [Fact]
        public void GivenPrimaryKeyRangeWithMultipleNextTypes_WhenVisitBinary_ThenGeneratesInClauseWithMultipleIds()
        {
            // Arrange
            var currentValue = new PrimaryKeyValue(1, 100);
            var nextResourceTypeIds = new BitArray(10);
            nextResourceTypeIds[2] = true;
            nextResourceTypeIds[3] = true;
            nextResourceTypeIds[7] = true;

            var primaryKeyRange = new PrimaryKeyRange(currentValue, nextResourceTypeIds);
            var expression = new BinaryExpression(BinaryOperator.GreaterThan, SqlFieldName.PrimaryKey, null, primaryKeyRange);
            var context = CreateContext();

            // Act
            PrimaryKeyRangeParameterQueryGenerator.Instance.VisitBinary(expression, context);

            // Assert
            var sql = context.StringBuilder.ToString();
            Assert.Contains("ResourceTypeId", sql);
            Assert.Contains("IN", sql);

            // Multiple values should be comma-separated
            Assert.Contains(",", sql);
        }

        [Fact]
        public void GivenPrimaryKeyRange_WhenVisitBinary_ThenParametersNotIncludedInHash()
        {
            // Arrange - Primary key range parameters should not be hashed for query plan reuse
            var currentValue = new PrimaryKeyValue(1, 100);
            var nextResourceTypeIds = new BitArray(5);
            nextResourceTypeIds[2] = true;

            var primaryKeyRange = new PrimaryKeyRange(currentValue, nextResourceTypeIds);
            var expression = new BinaryExpression(BinaryOperator.GreaterThan, SqlFieldName.PrimaryKey, null, primaryKeyRange);
            var context = CreateContext();

            var initialHashedParams = context.Parameters.ParametersToHash.Count;

            // Act
            PrimaryKeyRangeParameterQueryGenerator.Instance.VisitBinary(expression, context);

            // Assert - Parameters added should not increase the hash count
            Assert.Equal(initialHashedParams, context.Parameters.ParametersToHash.Count);
        }

        [Fact]
        public void GivenPrimaryKeyRangeWithLargeResourceTypeIdArray_WhenVisitBinary_ThenHandlesCorrectly()
        {
            // Arrange
            var currentValue = new PrimaryKeyValue(1, 100);
            var nextResourceTypeIds = new BitArray(100);
            nextResourceTypeIds[10] = true;
            nextResourceTypeIds[20] = true;
            nextResourceTypeIds[50] = true;
            nextResourceTypeIds[99] = true;

            var primaryKeyRange = new PrimaryKeyRange(currentValue, nextResourceTypeIds);
            var expression = new BinaryExpression(BinaryOperator.GreaterThan, SqlFieldName.PrimaryKey, null, primaryKeyRange);
            var context = CreateContext();

            // Act
            PrimaryKeyRangeParameterQueryGenerator.Instance.VisitBinary(expression, context);

            // Assert
            var sql = context.StringBuilder.ToString();
            Assert.Contains("ResourceTypeId", sql);
            Assert.Contains("IN", sql);
            Assert.NotEmpty(sql);
        }

        [Fact]
        public void GivenPrimaryKeyRangeWithAllNextTypesSet_WhenVisitBinary_ThenGeneratesLargeInClause()
        {
            // Arrange
            var currentValue = new PrimaryKeyValue(1, 100);
            var nextResourceTypeIds = new BitArray(10);
            for (int i = 0; i < 10; i++)
            {
                nextResourceTypeIds[i] = true;
            }

            var primaryKeyRange = new PrimaryKeyRange(currentValue, nextResourceTypeIds);
            var expression = new BinaryExpression(BinaryOperator.GreaterThan, SqlFieldName.PrimaryKey, null, primaryKeyRange);
            var context = CreateContext();

            // Act
            PrimaryKeyRangeParameterQueryGenerator.Instance.VisitBinary(expression, context);

            // Assert
            var sql = context.StringBuilder.ToString();
            Assert.Contains("IN", sql);

            // Should have multiple comma-separated values
            var commaCount = sql.Split(',').Length - 1;
            Assert.True(commaCount >= 9, $"Expected at least 9 commas for 10 values, but found {commaCount}");
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

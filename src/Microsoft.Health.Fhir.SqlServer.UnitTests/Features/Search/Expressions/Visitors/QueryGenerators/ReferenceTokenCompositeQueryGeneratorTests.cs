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
    /// Unit tests for ReferenceTokenCompositeQueryGenerator.
    /// Tests the generator's ability to delegate to component generators and handle composite search parameters.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ReferenceTokenCompositeQueryGeneratorTests
    {
        private readonly ISqlServerFhirModel _model;
        private readonly SchemaInformation _schemaInformation;

        public ReferenceTokenCompositeQueryGeneratorTests()
        {
            _model = Substitute.For<ISqlServerFhirModel>();
            _schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
            _schemaInformation.Current = SchemaVersionConstants.Max;
        }

        [Fact]
        public void GivenReferenceTokenCompositeQueryGenerator_WhenInstanceAccessed_ThenNotNull()
        {
            Assert.NotNull(ReferenceTokenCompositeQueryGenerator.Instance);
        }

        [Fact]
        public void GivenReferenceTokenCompositeQueryGenerator_WhenInstanceAccessedMultipleTimes_ThenReturnsSameInstance()
        {
            var instance1 = ReferenceTokenCompositeQueryGenerator.Instance;
            var instance2 = ReferenceTokenCompositeQueryGenerator.Instance;

            Assert.Same(instance1, instance2);
        }

        [Fact]
        public void GivenReferenceTokenCompositeQueryGenerator_WhenTableAccessed_ThenReturnsReferenceTokenCompositeSearchParamTable()
        {
            var table = ReferenceTokenCompositeQueryGenerator.Instance.Table;

            Assert.NotNull(table);
            Assert.Equal(VLatest.ReferenceTokenCompositeSearchParam.TableName, table.TableName);
        }

        [Fact]
        public void GivenStringExpressionForReferenceResourceIdWithComponentIndex0_WhenVisitString_ThenDelegatesToReferenceQueryGenerator()
        {
            // Arrange - Component index 0 should delegate to ReferenceQueryGenerator
            var expression = new StringExpression(
                StringOperator.Equals,
                FieldName.ReferenceResourceId,
                componentIndex: 0,
                value: "patient123",
                ignoreCase: false);

            var context = CreateContext();

            // Act
            ReferenceTokenCompositeQueryGenerator.Instance.VisitString(expression, context);

            // Assert
            var sql = context.StringBuilder.ToString();
            Assert.NotEmpty(sql);

            // ReferenceQueryGenerator should generate SQL for reference resource ID with correct component column
            Assert.Matches(@"ReferenceResourceId1\s*=\s*@\w+", sql);
        }

        [Fact]
        public void GivenStringExpressionForTokenCodeWithComponentIndex1_WhenVisitString_ThenDelegatesToTokenQueryGenerator()
        {
            // Arrange - Component index 1 should delegate to TokenQueryGenerator
            var expression = new StringExpression(
                StringOperator.Equals,
                FieldName.TokenCode,
                componentIndex: 1,
                value: "active",
                ignoreCase: false);

            var context = CreateContext();

            // Act
            ReferenceTokenCompositeQueryGenerator.Instance.VisitString(expression, context);

            // Assert
            var sql = context.StringBuilder.ToString();
            Assert.NotEmpty(sql);

            // TokenQueryGenerator should generate SQL for token code on component 2
            Assert.Matches(@"Code2\s*=\s*@\w+", sql);
        }

        [Fact]
        public void GivenMissingFieldExpressionWithComponentIndex0_WhenVisitMissingField_ThenDelegatesToReferenceQueryGenerator()
        {
            // Arrange
            var expression = new MissingFieldExpression(FieldName.ReferenceBaseUri, componentIndex: 0);
            var context = CreateContext();

            // Act
            ReferenceTokenCompositeQueryGenerator.Instance.VisitMissingField(expression, context);

            // Assert
            var sql = context.StringBuilder.ToString();
            Assert.NotEmpty(sql);

            // Should check for NULL base URI on component 1
            Assert.Contains("BaseUri1 IS NULL", sql);
        }

        [Fact]
        public void GivenMissingFieldExpressionWithComponentIndex1_WhenVisitMissingField_ThenDelegatesToTokenQueryGenerator()
        {
            // Arrange
            var expression = new MissingFieldExpression(FieldName.TokenSystem, componentIndex: 1);
            var context = CreateContext();

            // Act
            ReferenceTokenCompositeQueryGenerator.Instance.VisitMissingField(expression, context);

            // Assert
            var sql = context.StringBuilder.ToString();
            Assert.NotEmpty(sql);

            // Should check for NULL token system on component 2
            Assert.Contains("SystemId2 IS NULL", sql);
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

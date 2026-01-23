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
    /// Unit tests for ReferenceQueryGenerator.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ReferenceQueryGeneratorTests
    {
        private readonly ISqlServerFhirModel _model;
        private readonly SchemaInformation _schemaInformation;

        public ReferenceQueryGeneratorTests()
        {
            _model = Substitute.For<ISqlServerFhirModel>();
            _schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
            _schemaInformation.Current = SchemaVersionConstants.Max;
        }

        [Fact]
        public void GivenReferenceQueryGenerator_WhenInstanceAccessed_ThenNotNull()
        {
            Assert.NotNull(ReferenceQueryGenerator.Instance);
        }

        [Fact]
        public void GivenReferenceQueryGenerator_WhenTableAccessed_ThenReturnsReferenceSearchParamTable()
        {
            var table = ReferenceQueryGenerator.Instance.Table;

            Assert.Equal(VLatest.ReferenceSearchParam.TableName, table.TableName);
        }

        [Fact]
        public void GivenReferenceResourceIdExpression_WhenVisited_ThenGeneratesCorrectSqlQuery()
        {
            var expression = new StringExpression(StringOperator.Equals, FieldName.ReferenceResourceId, null, "patient-123", true);
            var context = CreateContext();

            ReferenceQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.ReferenceSearchParam.ReferenceResourceId.Metadata.Name, sql);
            Assert.True(context.Parameters.HasParametersToHash);
        }

        [Fact]
        public void GivenReferenceResourceTypeExpression_WhenVisited_ThenUsesResourceTypeId()
        {
            const string resourceType = "Patient";
            const short resourceTypeId = 1;

            _model.GetResourceTypeId(resourceType).Returns(resourceTypeId);

            var expression = new StringExpression(StringOperator.Equals, FieldName.ReferenceResourceType, null, resourceType, true);
            var context = CreateContext();

            ReferenceQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.ReferenceSearchParam.ReferenceResourceTypeId.Metadata.Name, sql);
            Assert.Contains("=", sql);
            _model.Received(1).GetResourceTypeId(resourceType);
        }

        [Fact]
        public void GivenReferenceBaseUriExpression_WhenVisited_ThenGeneratesCorrectSqlQuery()
        {
            var expression = new StringExpression(StringOperator.Equals, FieldName.ReferenceBaseUri, null, "http://example.org", true);
            var context = CreateContext();

            ReferenceQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.ReferenceSearchParam.BaseUri.Metadata.Name, sql);
            Assert.True(context.Parameters.HasParametersToHash);
        }

        [Fact]
        public void GivenMissingFieldExpression_WhenVisited_ThenChecksBaseUriIsNull()
        {
            var expression = new MissingFieldExpression(FieldName.ReferenceBaseUri, null);
            var context = CreateContext();

            ReferenceQueryGenerator.Instance.VisitMissingField(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.ReferenceSearchParam.BaseUri.Metadata.Name, sql);
            Assert.Contains("IS NULL", sql);
        }

        [Fact]
        public void GivenInvalidFieldName_WhenVisited_ThenThrowsArgumentOutOfRangeException()
        {
            var expression = new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "invalid", true);
            var context = CreateContext();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ReferenceQueryGenerator.Instance.VisitString(expression, context));
        }

        [Fact]
        public void GivenReferenceExpressionWithTableAlias_WhenVisited_ThenSqlContainsTableAlias()
        {
            const string tableAlias = "ref";
            var expression = new StringExpression(StringOperator.Equals, FieldName.ReferenceResourceId, null, "patient-123", true);
            var context = CreateContext(tableAlias);

            ReferenceQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains($"{tableAlias}.{VLatest.ReferenceSearchParam.ReferenceResourceId.Metadata.Name}", sql);
        }

        [Theory]
        [InlineData("Patient")]
        [InlineData("Observation")]
        [InlineData("Encounter")]
        public void GivenVariousResourceTypes_WhenVisited_ThenCallsModelCorrectly(string resourceType)
        {
            _model.GetResourceTypeId(resourceType).Returns((short)1);

            var expression = new StringExpression(StringOperator.Equals, FieldName.ReferenceResourceType, null, resourceType, true);
            var context = CreateContext();

            ReferenceQueryGenerator.Instance.VisitString(expression, context);

            _model.Received(1).GetResourceTypeId(resourceType);
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

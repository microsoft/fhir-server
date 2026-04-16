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
    /// Unit tests for UriQueryGenerator.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class UriQueryGeneratorTests
    {
        private readonly ISqlServerFhirModel _model;
        private readonly SchemaInformation _schemaInformation;

        public UriQueryGeneratorTests()
        {
            _model = Substitute.For<ISqlServerFhirModel>();
            _schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
            _schemaInformation.Current = SchemaVersionConstants.Max;
        }

        [Fact]
        public void GivenUriQueryGenerator_WhenInstanceAccessed_ThenNotNull()
        {
            Assert.NotNull(UriQueryGenerator.Instance);
        }

        [Fact]
        public void GivenUriQueryGenerator_WhenTableAccessed_ThenReturnsUriSearchParamTable()
        {
            var table = UriQueryGenerator.Instance.Table;

            Assert.Equal(VLatest.UriSearchParam.TableName, table.TableName);
        }

        [Fact]
        public void GivenUriEqualsExpression_WhenVisited_ThenGeneratesCorrectSqlQuery()
        {
            var expression = new StringExpression(StringOperator.Equals, FieldName.Uri, null, "http://example.org", true);
            var context = CreateContext();

            UriQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.UriSearchParam.Uri.Metadata.Name, sql);
            Assert.Matches($@"{VLatest.UriSearchParam.Uri.Metadata.Name}\s*=\s*@\w+", sql);
            Assert.True(context.Parameters.HasParametersToHash);
        }

        [Theory]
        [InlineData("http://example.org")]
        [InlineData("https://hl7.org/fhir/ValueSet/example")]
        [InlineData("urn:oid:1.2.3.4.5")]
        public void GivenVariousUriValues_WhenVisited_ThenGeneratesSQL(string uriValue)
        {
            var expression = new StringExpression(StringOperator.Equals, FieldName.Uri, null, uriValue, true);
            var context = CreateContext();

            UriQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.NotEmpty(sql);
            Assert.Contains(VLatest.UriSearchParam.Uri.Metadata.Name, sql);
            Assert.True(context.Parameters.HasParametersToHash);
        }

        [Fact]
        public void GivenUriExpressionWithTableAlias_WhenVisited_ThenSqlContainsTableAlias()
        {
            const string tableAlias = "uri";
            var expression = new StringExpression(StringOperator.Equals, FieldName.Uri, null, "http://example.org", true);
            var context = CreateContext(tableAlias);

            UriQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains($"{tableAlias}.{VLatest.UriSearchParam.Uri.Metadata.Name}", sql);
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

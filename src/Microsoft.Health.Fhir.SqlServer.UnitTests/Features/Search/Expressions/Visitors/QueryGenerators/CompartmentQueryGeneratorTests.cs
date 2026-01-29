// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Core.Extensions;
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
    /// Unit tests for CompartmentQueryGenerator.
    /// Tests the generator's ability to create SQL queries for compartment searches.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class CompartmentQueryGeneratorTests : IClassFixture<ModelInfoProviderFixture>
    {
        private readonly ISqlServerFhirModel _model;
        private readonly SchemaInformation _schemaInformation;

        public CompartmentQueryGeneratorTests(ModelInfoProviderFixture fixture)
        {
            _model = Substitute.For<ISqlServerFhirModel>();
            _schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
            _schemaInformation.Current = SchemaVersionConstants.Max;
        }

        [Fact]
        public void GivenCompartmentQueryGenerator_WhenInstanceAccessed_ThenNotNull()
        {
            Assert.NotNull(CompartmentQueryGenerator.Instance);
        }

        [Fact]
        public void GivenCompartmentQueryGenerator_WhenTableAccessed_ThenReturnsCompartmentAssignmentTable()
        {
            var table = CompartmentQueryGenerator.Instance.Table;

            Assert.Equal(VLatest.CompartmentAssignment.TableName, table.TableName);
        }

        [Theory]
        [InlineData("Patient", "123")]
        [InlineData("Encounter", "abc-def")]
        [InlineData("Device", "device-001")]
        [InlineData("Practitioner", "pract-xyz")]
        [InlineData("RelatedPerson", "rel-123-456")]
        public void GivenCompartmentSearchExpression_WhenVisited_ThenGeneratesCorrectSqlQuery(string compartmentType, string compartmentId)
        {
            byte compartmentTypeId = 1;
            _model.GetCompartmentTypeId(compartmentType).Returns(compartmentTypeId);

            var expression = new CompartmentSearchExpression(compartmentType, compartmentId);
            var context = CreateContext();

            CompartmentQueryGenerator.Instance.VisitCompartment(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.CompartmentAssignment.CompartmentTypeId.Metadata.Name, sql);
            Assert.Matches($@"{VLatest.CompartmentAssignment.CompartmentTypeId.Metadata.Name}\s*=\s*@\w+", sql);
            Assert.Contains(VLatest.CompartmentAssignment.ReferenceResourceId.Metadata.Name, sql);
            Assert.Matches($@"{VLatest.CompartmentAssignment.ReferenceResourceId.Metadata.Name}\s*=\s*@\w+", sql);
            Assert.Contains("AND", sql);

            _model.Received(1).GetCompartmentTypeId(compartmentType);
        }

        [Fact]
        public void GivenCompartmentSearchExpression_WhenVisited_ThenAddsParametersCorrectly()
        {
            const string compartmentType = "Patient";
            const string compartmentId = "123";
            byte compartmentTypeId = 1;

            _model.GetCompartmentTypeId(compartmentType).Returns(compartmentTypeId);

            var expression = new CompartmentSearchExpression(compartmentType, compartmentId);
            var context = CreateContext();

            CompartmentQueryGenerator.Instance.VisitCompartment(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Matches($@"{VLatest.CompartmentAssignment.CompartmentTypeId.Metadata.Name}\s*=\s*@\w+", sql);
            Assert.Matches($@"{VLatest.CompartmentAssignment.ReferenceResourceId.Metadata.Name}\s*=\s*@\w+", sql);
            Assert.Contains("AND", sql);
            Assert.NotEmpty(sql);
            Assert.True(context.Parameters.HasParametersToHash);
        }

        [Fact]
        public void GivenCompartmentSearchExpression_WhenVisited_ThenGeneratesCompleteQuery()
        {
            const string compartmentType = "Encounter";
            const string compartmentId = "enc-456";
            byte compartmentTypeId = 3;

            _model.GetCompartmentTypeId(compartmentType).Returns(compartmentTypeId);

            var expression = new CompartmentSearchExpression(compartmentType, compartmentId);
            var context = CreateContext();

            CompartmentQueryGenerator.Instance.VisitCompartment(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.CompartmentAssignment.CompartmentTypeId.Metadata.Name, sql);
            Assert.Contains(VLatest.CompartmentAssignment.ReferenceResourceId.Metadata.Name, sql);
            Assert.Contains("=", sql);
            Assert.Contains("AND", sql);
            Assert.NotEmpty(sql);
        }

        [Theory]
        [InlineData("ca")]
        [InlineData("compartment")]
        [InlineData("c1")]
        public void GivenCompartmentSearchExpressionWithTableAlias_WhenVisited_ThenSqlContainsTableAlias(string tableAlias)
        {
            const string compartmentType = "Encounter";
            const string compartmentId = "enc-456";
            byte compartmentTypeId = 3;

            _model.GetCompartmentTypeId(compartmentType).Returns(compartmentTypeId);

            var expression = new CompartmentSearchExpression(compartmentType, compartmentId);
            var context = CreateContext(tableAlias);

            CompartmentQueryGenerator.Instance.VisitCompartment(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains($"{tableAlias}.{VLatest.CompartmentAssignment.CompartmentTypeId.Metadata.Name}", sql);
            Assert.Contains($"{tableAlias}.{VLatest.CompartmentAssignment.ReferenceResourceId.Metadata.Name}", sql);
        }

        [Theory]
        [InlineData("Patient", 1)]
        [InlineData("Encounter", 2)]
        [InlineData("Device", 3)]
        [InlineData("Practitioner", 4)]
        [InlineData("RelatedPerson", 5)]
        public void GivenDifferentCompartmentTypes_WhenVisited_ThenUsesCorrectCompartmentTypeId(string compartmentType, byte expectedId)
        {
            _model.GetCompartmentTypeId(compartmentType).Returns(expectedId);

            var expression = new CompartmentSearchExpression(compartmentType, "123");
            var context = CreateContext();

            CompartmentQueryGenerator.Instance.VisitCompartment(expression, context);

            var sql = context.StringBuilder.ToString();

            _model.Received(1).GetCompartmentTypeId(compartmentType);
            Assert.NotEmpty(sql);
            Assert.True(context.Parameters.HasParametersToHash);
        }

        [Theory]
        [InlineData("Patient", 1)]
        [InlineData("Encounter", 2)]
        [InlineData("Device", 3)]
        [InlineData("Practitioner", 4)]
        [InlineData("RelatedPerson", 5)]
        public void GivenDifferentCompartmentTypes_WhenVisited_ThenCallsModelCorrectly(string compartmentType, byte expectedId)
        {
            _model.GetCompartmentTypeId(compartmentType).Returns(expectedId);

            var expression = new CompartmentSearchExpression(compartmentType, "123");
            var context = CreateContext();

            CompartmentQueryGenerator.Instance.VisitCompartment(expression, context);

            var sql = context.StringBuilder.ToString();

            _model.Received(1).GetCompartmentTypeId(compartmentType);

            Assert.NotEmpty(sql);
            Assert.Contains(VLatest.CompartmentAssignment.CompartmentTypeId.Metadata.Name, sql);
        }

        [Fact]
        public void GivenCompartmentSearchExpression_WhenVisited_ThenReturnsContext()
        {
            var expression = new CompartmentSearchExpression("Patient", "123");
            var context = CreateContext();
            _model.GetCompartmentTypeId("Patient").Returns((byte)1);

            var initialLength = context.StringBuilder.ToString().Length;

            CompartmentQueryGenerator.Instance.VisitCompartment(expression, context);

            var finalLength = context.StringBuilder.ToString().Length;
            Assert.True(finalLength > initialLength, "StringBuilder should have content added");
        }

        [Fact]
        public void GivenCompartmentSearchExpressionWithSpecialCharacters_WhenVisited_ThenHandlesCorrectly()
        {
            const string compartmentId = "patient-123/abc:xyz";
            _model.GetCompartmentTypeId("Patient").Returns((byte)1);

            var expression = new CompartmentSearchExpression("Patient", compartmentId);
            var context = CreateContext();

            CompartmentQueryGenerator.Instance.VisitCompartment(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.NotEmpty(sql);
            Assert.Contains(VLatest.CompartmentAssignment.CompartmentTypeId.Metadata.Name, sql);
            Assert.Contains(VLatest.CompartmentAssignment.ReferenceResourceId.Metadata.Name, sql);
            Assert.Contains("AND", sql);
            Assert.True(context.Parameters.HasParametersToHash);
        }

        [Fact]
        public void GivenMultipleCompartmentSearchExpressions_WhenVisited_ThenEachGeneratesSQL()
        {
            var expression1 = new CompartmentSearchExpression("Patient", "patient-1");
            var expression2 = new CompartmentSearchExpression("Encounter", "encounter-1");

            _model.GetCompartmentTypeId("Patient").Returns((byte)1);
            _model.GetCompartmentTypeId("Encounter").Returns((byte)2);

            var context1 = CreateContext();
            var context2 = CreateContext();

            CompartmentQueryGenerator.Instance.VisitCompartment(expression1, context1);
            CompartmentQueryGenerator.Instance.VisitCompartment(expression2, context2);

            var sql1 = context1.StringBuilder.ToString();
            var sql2 = context2.StringBuilder.ToString();

            Assert.Contains(VLatest.CompartmentAssignment.CompartmentTypeId.Metadata.Name, sql1);
            Assert.Contains(VLatest.CompartmentAssignment.ReferenceResourceId.Metadata.Name, sql1);
            Assert.Contains("AND", sql1);

            Assert.Contains(VLatest.CompartmentAssignment.CompartmentTypeId.Metadata.Name, sql2);
            Assert.Contains(VLatest.CompartmentAssignment.ReferenceResourceId.Metadata.Name, sql2);
            Assert.Contains("AND", sql2);

            _model.Received(1).GetCompartmentTypeId("Patient");
            _model.Received(1).GetCompartmentTypeId("Encounter");

            Assert.True(context1.Parameters.HasParametersToHash);
            Assert.True(context2.Parameters.HasParametersToHash);
        }

        [Fact]
        public void GivenCompartmentSearchExpression_WhenParametersAreHashable_ThenParametersIncludedInHash()
        {
            _model.GetCompartmentTypeId("Patient").Returns((byte)1);

            var expression = new CompartmentSearchExpression("Patient", "123");
            var context = CreateContext();

            CompartmentQueryGenerator.Instance.VisitCompartment(expression, context);

            var hashingParams = context.Parameters;
            Assert.True(hashingParams.HasParametersToHash);
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

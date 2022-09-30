// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class IncludeRewriterTests : IClassFixture<IncludeRewriterTests.IncludeRewriterFixture>, IAsyncLifetime
    {
        private readonly IncludeRewriterFixture _fixture;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;

        public IncludeRewriterTests(IncludeRewriterFixture fixture)
        {
            _fixture = fixture;
            _searchParameterDefinitionManager = fixture.SearchParameterDefinitionManager;
        }

        public async Task InitializeAsync()
        {
            await _fixture.Start();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        // Basic Queries with 0-2 include search parameters with all the pair combinations

        [Fact]
        public void GivenASqlRootExpressionWithoutIncludes_WhenVisitedByIncludeRewriter_TheSameExpressionShouldBeReturnedAsIs()
        {
            // Leave the query as is if there's no Include expression. For example:
            // [base]/Patient?gender=female&family=Ellison

            Expression predicate = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "Patient", false)),
                    new SearchParameterExpression(new SearchParameterInfo("gender", "gender"), new StringExpression(StringOperator.Equals, FieldName.String, null, "female", false)),
                    new SearchParameterExpression(new SearchParameterInfo("family", "family"), new StringExpression(StringOperator.Equals, FieldName.String, null, "Ellison", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All),
                    new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
                },
                new List<SearchParameterExpressionBase>());

            var rewrittenExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).SearchParamTableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(rewrittenExpressions);
            Assert.Equal(2, rewrittenExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.All, rewrittenExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Top, rewrittenExpressions[1].Kind);
        }

        [Fact]
        public void GivenASqlRootExpressionWithTwoIncludes_WhenVisitedByIncludeRewriter_TheOrderDoesNotMatterAndShouldRemainUnchanged()
        {
            // Order the following query:
            // [base]/MedicationDispense?_include=MedicationDispense:prescription&_include=MedicationDispense:patient&_id=smart-MedicationDispense-567

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "prescription");
            var includeMedicationDispensePrescription = new IncludeExpression(new[] { "MedicationDispense" }, refSearchParameter, "MedicationDispense", null, null, false, false, false);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "patient");
            var includeMedicationDispensePatient = new IncludeExpression(new[] { "MedicationDispense" }, refSearchParameter, "MedicationDispense", null, null, false, false, false);

            Expression predicate = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "MedicationDispense", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id", "_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "smart-MedicationDispense-567", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeMedicationDispensePrescription, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeMedicationDispensePatient, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
                },
                new List<SearchParameterExpressionBase>());

            var orderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).SearchParamTableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(orderedExpressions);
            Assert.Equal(7, orderedExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.All, orderedExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Top, orderedExpressions[1].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)orderedExpressions[2].Predicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceTypes[0]);
            Assert.Equal("prescription", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[3].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[4].Predicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceTypes[0]);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[5].Kind);

            Assert.Equal(SearchParamTableExpressionKind.IncludeUnionAll, orderedExpressions[6].Kind);
        }

        [Fact]
        public void GivenASqlRootExpressionWithOneIncludeAndOneIncludeIterate_WhenVisitedByIncludeRewriter_TheExpressionsShouldBeOrderedCorrectly()
        {
            // Order the following query:
            // [base]/MedicationDispense?_include:iterate=Patient:general-practitioner&_include=MedicationRequest:patient&_id=12345

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("Patient", "general-practitioner");
            var includeIteratePatientGeneralPractitioner = new IncludeExpression(new[] { "Patient" }, refSearchParameter, "Patient", null, null, false, false, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "patient");
            var includeMedicationDispensePatient = new IncludeExpression(new[] { "MedicationDispense" }, refSearchParameter, "MedicationDispense", null, null, false, false, false);

            Expression predicate = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "MedicationDispense", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id", "_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "12345", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIteratePatientGeneralPractitioner, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeMedicationDispensePatient, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
                },
                new List<SearchParameterExpressionBase>());

            var orderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).SearchParamTableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(orderedExpressions);
            Assert.Equal(7, orderedExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.All, orderedExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Top, orderedExpressions[1].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)orderedExpressions[2].Predicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceTypes[0]);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[3].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[4].Predicate;
            Assert.Equal("Patient", includeExpression.ResourceTypes[0]);
            Assert.Equal("general-practitioner", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[5].Kind);

            Assert.Equal(SearchParamTableExpressionKind.IncludeUnionAll, orderedExpressions[6].Kind);
        }

        [Fact]
        public void GivenASqlRootExpressionWithOneIncludeAndOneRevInclude_WhenVisitedByIncludeRewriter_TheOrderDoesNotMatterAndShouldRemainUnchanged()
        {
            // Order the following query:
            // [base]/MedicationRequest?_include=MedicationRequest:patient&_revinclude=MedicationDispense:prescription&_id=12345

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationRequest", "patient");
            var includeMedicationRequestPatient = new IncludeExpression(new[] { "MedicationRequest" }, refSearchParameter, "MedicationRequest", null, null, false, false, false);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "prescription");
            var revincludeMedicationDispensePrescription = new IncludeExpression(new[] { "MedicationDispense" }, refSearchParameter, "MedicationDispense", null, null, false, true, false);

            Expression predicate = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "MedicationRequest", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id", "_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "12345", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeMedicationRequestPatient, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  revincludeMedicationDispensePrescription, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
                },
                new List<SearchParameterExpressionBase>());

            var orderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).SearchParamTableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(orderedExpressions);
            Assert.Equal(7, orderedExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.All, orderedExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Top, orderedExpressions[1].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)orderedExpressions[2].Predicate;
            Assert.Equal("MedicationRequest", includeExpression.ResourceTypes[0]);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[3].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[4].Predicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceTypes[0]);
            Assert.Equal("prescription", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[5].Kind);

            Assert.Equal(SearchParamTableExpressionKind.IncludeUnionAll, orderedExpressions[6].Kind);
        }

        [Fact]
        public void GivenASqlRootExpressionWithOneIncludeAndOneRevIncludeIterate_WhenVisitedByIncludeRewriter_TheExpressionsShouldBeOrderedCorrectly()
        {
            // Order the following query:
            // [base]/MedicationRequest?_revinclude:iterate=MedicationDispense:patient&_include=MedicationRequest:patient&_id=12345

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "patient");
            var revincludeIterateMedicationDispensePatient = new IncludeExpression(new[] { "MedicationDispense" }, refSearchParameter, "MedicationDispense", null, null, false, true, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationRequest", "patient");
            var includeMedicationRequestPatient = new IncludeExpression(new[] { "MedicationRequest" }, refSearchParameter, "MedicationRequest", null, null, false, false, false);

            Expression predicate = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "MedicationRequest", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id", "_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "12345", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  revincludeIterateMedicationDispensePatient, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeMedicationRequestPatient, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
                },
                new List<SearchParameterExpressionBase>());

            var orderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).SearchParamTableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(orderedExpressions);
            Assert.Equal(7, orderedExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.All, orderedExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Top, orderedExpressions[1].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)orderedExpressions[2].Predicate;
            Assert.Equal("MedicationRequest", includeExpression.ResourceTypes[0]);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[3].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[4].Predicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceTypes[0]);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[5].Kind);

            Assert.Equal(SearchParamTableExpressionKind.IncludeUnionAll, orderedExpressions[6].Kind);
        }

        [Fact]
        public void GivenASqlRootExpressionWithTwoRevIncludes_WhenVisitedByIncludeRewriter_TheOrderDoesNotMatterAndShouldRemainUnchanged()
        {
            // Order the following query:
            // [base]/Patient?_revinclude=MedicationDispense:patient&_revinclude=MedicationRequest:patient&_id=patientId

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "patient");
            var revincludeMedicationDispensePatient = new IncludeExpression(new[] { "MedicationDispense" }, refSearchParameter, "MedicationDispense", null, null, false, true, false);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationRequest", "patient");
            var revincludeMedicationRequestPatient = new IncludeExpression(new[] { "MedicationRequest" }, refSearchParameter, "MedicationRequest", null, null, false, true, false);

            Expression predicate = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "Patient", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id", "_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "patientId", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  revincludeMedicationDispensePatient, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  revincludeMedicationRequestPatient, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
                },
                new List<SearchParameterExpressionBase>());

            var orderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).SearchParamTableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(orderedExpressions);
            Assert.Equal(7, orderedExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.All, orderedExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Top, orderedExpressions[1].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)orderedExpressions[2].Predicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceTypes[0]);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[3].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[4].Predicate;
            Assert.Equal("MedicationRequest", includeExpression.ResourceTypes[0]);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[5].Kind);

            Assert.Equal(SearchParamTableExpressionKind.IncludeUnionAll, orderedExpressions[6].Kind);
        }

        [Fact]
        public void GivenASqlRootExpressionWithTwoIncludesSpecifyingTargetType_WhenVisitedByIncludeRewriter_TheOrderDoesNotMatterAndShouldRemainUnchanged()
        {
            // Order the following query:
            // [base]/MedicationDispense?_include=MedicationDispense:prescription&_include=MedicationDispense:subject:Patient&_id=smart-MedicationDispense-567

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "prescription");
            var includeMedicationDispensePrescription = new IncludeExpression(new[] { "MedicationDispense" }, refSearchParameter, "MedicationDispense", null, null, false, false, false);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "subject");
            var includeMedicationDispensePatient = new IncludeExpression(new[] { "MedicationDispense" }, refSearchParameter, "MedicationDispense", "Patient", null, false, false, false);

            Expression predicate = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "MedicationDispense", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id", "_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "smart-MedicationDispense-567", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeMedicationDispensePrescription, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeMedicationDispensePatient, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
                },
                new List<SearchParameterExpressionBase>());

            var orderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).SearchParamTableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(orderedExpressions);
            Assert.Equal(7, orderedExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.All, orderedExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Top, orderedExpressions[1].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)orderedExpressions[2].Predicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceTypes[0]);
            Assert.Equal("prescription", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[3].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[4].Predicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceTypes[0]);
            Assert.Equal("subject", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[5].Kind);

            Assert.Equal(SearchParamTableExpressionKind.IncludeUnionAll, orderedExpressions[6].Kind);
        }

        [Fact]
        public void GivenASqlRootExpressionWithOneIncludeAndOneIncludeIterateSpecifyingTargetType_WhenVisitedByIncludeRewriter_TheExpressionsShouldBeOrderedCorrectly()
        {
            // Order the following query:
            // [base]/MedicationDispense?_include:iterate=Patient:general-practitioner:Practitioner&_include=MedicationRequest:patient&_id=12345

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("Patient", "general-practitioner");
            var includeIteratePatientGeneralPractitioner = new IncludeExpression(new[] { "Patient" }, refSearchParameter, "Patient", "Practitioner", null, false, false, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "patient");
            var includeMedicationDispensePatient = new IncludeExpression(new[] { "MedicationDispense" }, refSearchParameter, "MedicationDispense", null, null, false, false, false);

            Expression predicate = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "MedicationDispense", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id", "_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "12345", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIteratePatientGeneralPractitioner, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeMedicationDispensePatient, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
                },
                new List<SearchParameterExpressionBase>());

            var orderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).SearchParamTableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(orderedExpressions);
            Assert.Equal(7, orderedExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.All, orderedExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Top, orderedExpressions[1].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)orderedExpressions[2].Predicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceTypes[0]);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[3].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[4].Predicate;
            Assert.Equal("Patient", includeExpression.ResourceTypes[0]);
            Assert.Equal("general-practitioner", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[5].Kind);

            Assert.Equal(SearchParamTableExpressionKind.IncludeUnionAll, orderedExpressions[6].Kind);
        }

        [Fact]
        public void GivenASqlRootExpressionWithOneIncludeAndOneRevIncludeSpecifyingTargetType_WhenVisitedByIncludeRewriter_TheOrderDoesNotMatterAndShouldRemainUnchanged()
        {
            // Order the following query:
            // [base]/MedicationRequest?_include=MedicationRequest:patient&_revinclude=MedicationDispense:prescription:MedicationRequest&_id=12345

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationRequest", "patient");
            var includeMedicationRequestPatient = new IncludeExpression(new[] { "MedicationRequest" }, refSearchParameter, "MedicationRequest", null, null, false, false, false);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "prescription");
            var revincludeMedicationDispensePrescription = new IncludeExpression(new[] { "MedicationDispense" }, refSearchParameter, "MedicationDispense", "MedicationRequest", null, false, true, false);

            Expression predicate = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "MedicationRequest", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id", "_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "12345", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeMedicationRequestPatient, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  revincludeMedicationDispensePrescription, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
                },
                new List<SearchParameterExpressionBase>());

            var orderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).SearchParamTableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(orderedExpressions);
            Assert.Equal(7, orderedExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.All, orderedExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Top, orderedExpressions[1].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)orderedExpressions[2].Predicate;
            Assert.Equal("MedicationRequest", includeExpression.ResourceTypes[0]);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[3].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[4].Predicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceTypes[0]);
            Assert.Equal("prescription", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[5].Kind);

            Assert.Equal(SearchParamTableExpressionKind.IncludeUnionAll, orderedExpressions[6].Kind);
        }

        [Fact]
        public void GivenASqlRootExpressionWithOneIncludeAndOneRevIncludeIterateSpecifyingTargetType_WhenVisitedByIncludeRewriter_TheExpressionsShouldBeOrderedCorrectly()
        {
            // Order the following query:
            // [base]/MedicationRequest?_revinclude:iterate=MedicationDispense:patient&_include=MedicationRequest:subject:Patient&_id=12345

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "patient");
            var revincludeIterateMedicationDispensePatient = new IncludeExpression(new[] { "MedicationDispense" }, refSearchParameter, "MedicationDispense", null, null, false, true, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationRequest", "subject");
            var includeMedicationRequestPatient = new IncludeExpression(new[] { "MedicationRequest" }, refSearchParameter, "MedicationRequest", "Patient", null, false, false, false);

            Expression predicate = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "MedicationRequest", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id", "_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "12345", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  revincludeIterateMedicationDispensePatient, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeMedicationRequestPatient, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
                },
                new List<SearchParameterExpressionBase>());

            var orderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).SearchParamTableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(orderedExpressions);
            Assert.Equal(7, orderedExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.All, orderedExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Top, orderedExpressions[1].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)orderedExpressions[2].Predicate;
            Assert.Equal("MedicationRequest", includeExpression.ResourceTypes[0]);
            Assert.Equal("subject", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[3].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[4].Predicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceTypes[0]);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[5].Kind);

            Assert.Equal(SearchParamTableExpressionKind.IncludeUnionAll, orderedExpressions[6].Kind);
        }

        [Fact]
        public void GivenASqlRootExpressionWithTwoRevIncludesSpecifyingTargetType_WhenVisitedByIncludeRewriter_TheOrderDoesNotMatterAndShouldRemainUnchanged()
        {
            // Order the following query:
            // [base]/Patient?_revinclude=MedicationDispense:subject:Patient&_revinclude=MedicationRequest:patient&_id=patientId

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "subject");
            var revincludeMedicationDispensePatient = new IncludeExpression(new[] { "MedicationDispense" }, refSearchParameter, "MedicationDispense", "Patient", null, false, true, false);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationRequest", "patient");
            var revincludeMedicationRequestPatient = new IncludeExpression(new[] { "MedicationRequest" }, refSearchParameter, "MedicationRequest", null, null, false, true, false);

            Expression predicate = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "Patient", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id", "_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "patientId", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  revincludeMedicationDispensePatient, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  revincludeMedicationRequestPatient, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
                },
                new List<SearchParameterExpressionBase>());

            var orderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).SearchParamTableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(orderedExpressions);
            Assert.Equal(7, orderedExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.All, orderedExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Top, orderedExpressions[1].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)orderedExpressions[2].Predicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceTypes[0]);
            Assert.Equal("subject", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[3].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[4].Predicate;
            Assert.Equal("MedicationRequest", includeExpression.ResourceTypes[0]);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[5].Kind);

            Assert.Equal(SearchParamTableExpressionKind.IncludeUnionAll, orderedExpressions[6].Kind);
        }

        [Fact]
        public void GivenASqlRootExpressionWithOneRevIncludeAndOneRevIncludeIterate_WhenVisitedByIncludeRewriter_TheExpressionsShouldBeOrderedCorrectly()
        {
            // Order the following query:
            // [base]/Practitioner?_revinclude:iterate=MedicationRequest:patient&_revinclude=Patient:general-practitioner&_id=12345

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationRequest", "patient");
            var revincludeIterateMedicationRequestPatient = new IncludeExpression(new[] { "MedicationRequest" }, refSearchParameter, "MedicationRequest", null, null, false, true, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("Patient", "general-practitioner");
            var revincludePatientGeneralPractitioner = new IncludeExpression(new[] { "Patient" }, refSearchParameter, "Patient", null, null, false, true, false);

            Expression predicate = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "Practitioner", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id", "_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "12345", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  revincludeIterateMedicationRequestPatient, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  revincludePatientGeneralPractitioner, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
                },
                new List<SearchParameterExpressionBase>());

            var orderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).SearchParamTableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(orderedExpressions);
            Assert.Equal(7, orderedExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.All, orderedExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Top, orderedExpressions[1].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)orderedExpressions[2].Predicate;
            Assert.Equal("Patient", includeExpression.ResourceTypes[0]);
            Assert.Equal("general-practitioner", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[3].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[4].Predicate;
            Assert.Equal("MedicationRequest", includeExpression.ResourceTypes[0]);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[5].Kind);

            Assert.Equal(SearchParamTableExpressionKind.IncludeUnionAll, orderedExpressions[6].Kind);
        }

        [Fact]
        public void GivenASqlRootExpressionWithOneRevIncludeAndOneIncludeIterate_WhenVisitedByIncludeRewriter_TheExpressionsShouldBeOrderedCorrectly()
        {
            // Order the following query:
            // [base]/MedicationRequest?_include:iterate=MedicationDispense:patient&_revinclude=MedicationDispense:prescription&_id=12345

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "patient");
            var includeIterateMedicationDispensePatient = new IncludeExpression(new[] { "MedicationDispense" }, refSearchParameter, "MedicationDispense", null, null, false, false, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "prescription");
            var revincludeMedicationDispensePrescription = new IncludeExpression(new[] { "MedicationDispense" }, refSearchParameter, "MedicationDispense", null, null, false, true, false);

            Expression predicate = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "MedicationRequest", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id", "_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "12345", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIterateMedicationDispensePatient, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  revincludeMedicationDispensePrescription, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
                },
                new List<SearchParameterExpressionBase>());

            var orderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).SearchParamTableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(orderedExpressions);
            Assert.Equal(7, orderedExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.All, orderedExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Top, orderedExpressions[1].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)orderedExpressions[2].Predicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceTypes[0]);
            Assert.Equal("prescription", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[3].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[4].Predicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceTypes[0]);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[5].Kind);

            Assert.Equal(SearchParamTableExpressionKind.IncludeUnionAll, orderedExpressions[6].Kind);
        }

        // Queries with indirect dependencies
        // All possible permutations of 3 parameters: _include=MedicationDispense:prescription&_include:iterate=MedicationRequest:patient&_include:iterate=Patient:general-practitioner

        [Fact]
        public void GivenASqlRootExpressionWithThreeIncludesFirstPermutation_WhenVisitedByIncludeRewriter_TheExpressionsShouldBeOrderedCorrectly()
        {
            // Order the following query:
            // [base]/MedicationDispense?_include=MedicationDispense:prescription&_include:iterate=MedicationRequest:patient&_include:iterate=Patient:general-practitioner&_id=12345

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "prescription");
            var includeMedicationDispensePrescription = new IncludeExpression(new[] { "MedicationDispense" }, refSearchParameter, "MedicationDispense", null, null, false, false, false);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationRequest", "patient");
            var includeIterateMedicationRequestPatient = new IncludeExpression(new[] { "MedicationRequest" }, refSearchParameter, "MedicationRequest", null, null, false, false, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("Patient", "general-practitioner");
            var includeIteratePatientGeneralPractitioner = new IncludeExpression(new[] { "Patient" }, refSearchParameter, "Patient", null, null, false, false, true);

            Expression predicate = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "MedicationDispense", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id", "_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "12345", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeMedicationDispensePrescription, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIterateMedicationRequestPatient, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIteratePatientGeneralPractitioner, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
                },
                new List<SearchParameterExpressionBase>());

            var orderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).SearchParamTableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(orderedExpressions);
            Assert.Equal(9, orderedExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.All, orderedExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Top, orderedExpressions[1].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)orderedExpressions[2].Predicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceTypes[0]);
            Assert.Equal("prescription", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[3].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[4].Predicate;
            Assert.Equal("MedicationRequest", includeExpression.ResourceTypes[0]);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[5].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[6].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[6].Predicate;
            Assert.Equal("Patient", includeExpression.ResourceTypes[0]);
            Assert.Equal("general-practitioner", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[7].Kind);

            Assert.Equal(SearchParamTableExpressionKind.IncludeUnionAll, orderedExpressions[8].Kind);
        }

        [Fact]
        public void GivenASqlRootExpressionWithThreeIncludesSecondPermutation_WhenVisitedByIncludeRewriter_TheExpressionsShouldBeOrderedCorrectly()
        {
            // Order the following query:
            // [base]/MedicationDispense?_include=MedicationDispense:prescription&_include:iterate=Patient:general-practitioner&_include:iterate=MedicationRequest:patient&_id=12345

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "prescription");
            var includeMedicationDispensePrescription = new IncludeExpression(new[] { "MedicationDispense" }, refSearchParameter, "MedicationDispense", null, null, false, false, false);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationRequest", "patient");
            var includeIterateMedicationRequestPatient = new IncludeExpression(new[] { "MedicationRequest" }, refSearchParameter, "MedicationRequest", null, null, false, false, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("Patient", "general-practitioner");
            var includeIteratePatientGeneralPractitioner = new IncludeExpression(new[] { "Patient" }, refSearchParameter, "Patient", null, null, false, false, true);

            Expression predicate = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "MedicationDispense", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id", "_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "12345", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeMedicationDispensePrescription, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIteratePatientGeneralPractitioner, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIterateMedicationRequestPatient, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
                },
                new List<SearchParameterExpressionBase>());

            var orderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).SearchParamTableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(orderedExpressions);
            Assert.Equal(9, orderedExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.All, orderedExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Top, orderedExpressions[1].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)orderedExpressions[2].Predicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceTypes[0]);
            Assert.Equal("prescription", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[3].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[4].Predicate;
            Assert.Equal("MedicationRequest", includeExpression.ResourceTypes[0]);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[5].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[6].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[6].Predicate;
            Assert.Equal("Patient", includeExpression.ResourceTypes[0]);
            Assert.Equal("general-practitioner", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[7].Kind);

            Assert.Equal(SearchParamTableExpressionKind.IncludeUnionAll, orderedExpressions[8].Kind);
        }

        [Fact]
        public void GivenASqlRootExpressionWithThreeIncludesThirdPermutation_WhenVisitedByIncludeRewriter_TheExpressionsShouldBeOrderedCorrectly()
        {
            // Order the following query:
            // [base]/MedicationDispense?_include:iterate=MedicationRequest:patient&_include=MedicationDispense:prescription&_include:iterate=Patient:general-practitioner&_id=12345

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "prescription");
            var includeMedicationDispensePrescription = new IncludeExpression(new[] { "MedicationDispense" }, refSearchParameter, "MedicationDispense", null, null, false, false, false);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationRequest", "patient");
            var includeIterateMedicationRequestPatient = new IncludeExpression(new[] { "MedicationRequest" }, refSearchParameter, "MedicationRequest", null, null, false, false, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("Patient", "general-practitioner");
            var includeIteratePatientGeneralPractitioner = new IncludeExpression(new[] { "Patient" }, refSearchParameter, "Patient", null, null, false, false, true);

            Expression predicate = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "MedicationDispense", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id", "_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "12345", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIterateMedicationRequestPatient, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeMedicationDispensePrescription, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIteratePatientGeneralPractitioner, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
                },
                new List<SearchParameterExpressionBase>());

            var orderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).SearchParamTableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(orderedExpressions);
            Assert.Equal(9, orderedExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.All, orderedExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Top, orderedExpressions[1].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)orderedExpressions[2].Predicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceTypes[0]);
            Assert.Equal("prescription", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[3].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[4].Predicate;
            Assert.Equal("MedicationRequest", includeExpression.ResourceTypes[0]);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[5].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[6].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[6].Predicate;
            Assert.Equal("Patient", includeExpression.ResourceTypes[0]);
            Assert.Equal("general-practitioner", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[7].Kind);

            Assert.Equal(SearchParamTableExpressionKind.IncludeUnionAll, orderedExpressions[8].Kind);
        }

        [Fact]
        public void GivenASqlRootExpressionWithThreeIncludesFourthPermutation_WhenVisitedByIncludeRewriter_TheExpressionsShouldBeOrderedCorrectly()
        {
            // Order the following query:
            // [base]/MedicationDispense?_include:iterate=MedicationRequest:patient&_include:iterate=Patient:general-practitioner&_include=MedicationDispense:prescription&_id=12345

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "prescription");
            var includeMedicationDispensePrescription = new IncludeExpression(new[] { "MedicationDispense" }, refSearchParameter, "MedicationDispense", null, null, false, false, false);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationRequest", "patient");
            var includeIterateMedicationRequestPatient = new IncludeExpression(new[] { "MedicationRequest" }, refSearchParameter, "MedicationRequest", null, null, false, false, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("Patient", "general-practitioner");
            var includeIteratePatientGeneralPractitioner = new IncludeExpression(new[] { "Patient" }, refSearchParameter, "Patient", null, null, false, false, true);

            Expression predicate = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "MedicationDispense", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id", "_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "12345", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIterateMedicationRequestPatient, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIteratePatientGeneralPractitioner, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeMedicationDispensePrescription, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
                },
                new List<SearchParameterExpressionBase>());

            var orderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).SearchParamTableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(orderedExpressions);
            Assert.Equal(9, orderedExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.All, orderedExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Top, orderedExpressions[1].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)orderedExpressions[2].Predicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceTypes[0]);
            Assert.Equal("prescription", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[3].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[4].Predicate;
            Assert.Equal("MedicationRequest", includeExpression.ResourceTypes[0]);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[5].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[6].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[6].Predicate;
            Assert.Equal("Patient", includeExpression.ResourceTypes[0]);
            Assert.Equal("general-practitioner", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[7].Kind);

            Assert.Equal(SearchParamTableExpressionKind.IncludeUnionAll, orderedExpressions[8].Kind);
        }

        [Fact]
        public void GivenASqlRootExpressionWithThreeIncludesFifthPermutation_WhenVisitedByIncludeRewriter_TheExpressionsShouldBeOrderedCorrectly()
        {
            // Order the following query:
            // [base]/MedicationDispense?_include:iterate=Patient:general-practitioner&_include=MedicationDispense:prescription&_include:iterate=MedicationRequest:patient&_id=12345

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "prescription");
            var includeMedicationDispensePrescription = new IncludeExpression(new[] { "MedicationDispense" }, refSearchParameter, "MedicationDispense", null, null, false, false, false);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationRequest", "patient");
            var includeIterateMedicationRequestPatient = new IncludeExpression(new[] { "MedicationRequest" }, refSearchParameter, "MedicationRequest", null, null, false, false, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("Patient", "general-practitioner");
            var includeIteratePatientGeneralPractitioner = new IncludeExpression(new[] { "Patient" }, refSearchParameter, "Patient", null, null, false, false, true);

            Expression predicate = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "MedicationDispense", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id", "_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "12345", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIteratePatientGeneralPractitioner, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeMedicationDispensePrescription, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIterateMedicationRequestPatient, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
                },
                new List<SearchParameterExpressionBase>());

            var orderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).SearchParamTableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(orderedExpressions);
            Assert.Equal(9, orderedExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.All, orderedExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Top, orderedExpressions[1].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)orderedExpressions[2].Predicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceTypes[0]);
            Assert.Equal("prescription", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[3].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[4].Predicate;
            Assert.Equal("MedicationRequest", includeExpression.ResourceTypes[0]);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[5].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[6].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[6].Predicate;
            Assert.Equal("Patient", includeExpression.ResourceTypes[0]);
            Assert.Equal("general-practitioner", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[7].Kind);

            Assert.Equal(SearchParamTableExpressionKind.IncludeUnionAll, orderedExpressions[8].Kind);
        }

        [Fact]
        public void GivenASqlRootExpressionWithThreeIncludesSixthPermutation_WhenVisitedByIncludeRewriter_TheExpressionsShouldBeOrderedCorrectly()
        {
            // Order the following query:
            // [base]/MedicationDispense?_include:iterate=Patient:general-practitioner&_include:iterate=MedicationRequest:patient&_include=MedicationDispense:prescription&_id=12345

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "prescription");
            var includeMedicationDispensePrescription = new IncludeExpression(new[] { "MedicationDispense" }, refSearchParameter, "MedicationDispense", null, null, false, false, false);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationRequest", "patient");
            var includeIterateMedicationRequestPatient = new IncludeExpression(new[] { "MedicationRequest" }, refSearchParameter, "MedicationRequest", null, null, false, false, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("Patient", "general-practitioner");
            var includeIteratePatientGeneralPractitioner = new IncludeExpression(new[] { "Patient" }, refSearchParameter, "Patient", null, null, false, false, true);

            Expression predicate = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "MedicationDispense", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id", "_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "12345", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIteratePatientGeneralPractitioner, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIterateMedicationRequestPatient, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeMedicationDispensePrescription, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
                },
                new List<SearchParameterExpressionBase>());

            var orderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).SearchParamTableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(orderedExpressions);
            Assert.Equal(9, orderedExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.All, orderedExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Top, orderedExpressions[1].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)orderedExpressions[2].Predicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceTypes[0]);
            Assert.Equal("prescription", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[3].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[4].Predicate;
            Assert.Equal("MedicationRequest", includeExpression.ResourceTypes[0]);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[5].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[6].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[6].Predicate;
            Assert.Equal("Patient", includeExpression.ResourceTypes[0]);
            Assert.Equal("general-practitioner", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[7].Kind);

            Assert.Equal(SearchParamTableExpressionKind.IncludeUnionAll, orderedExpressions[8].Kind);
        }

        // Queries with multiple includes/revincludes

        [Fact]
        public void GivenASqlRootExpressionWithMultipleIncludes_WhenVisitedByIncludeRewriter_TheExpressionsShouldBeOrderedCorrectly()
        {
            // Order the following query:
            // [base]/MedicationDispense?_include:iterate=Patient:general-practitioner&_include:iterate=MedicationRequest:patient&_include=MedicationDispense:prescription&_id=smart-MedicationDispense-567

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("Patient", "general-practitioner");
            var includeIteratePatientGeneralPractitioner = new IncludeExpression(new[] { "Patient" }, refSearchParameter, "Patient", null, null, false, false, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationRequest", "patient");
            var includeIterateMedicationRequestPatient = new IncludeExpression(new[] { "MedicationRequest" }, refSearchParameter, "MedicationRequest", null, null, false, false, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "prescription");
            var includeMedicationDispensePrescription = new IncludeExpression(new[] { "MedicationDispense" }, refSearchParameter, "MedicationDispense", null, null, false, false, false);

            Expression predicate = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "MedicationDispense", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id", "_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "smart-MedicationDispense-567", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIteratePatientGeneralPractitioner, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIterateMedicationRequestPatient, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeMedicationDispensePrescription, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
                },
                new List<SearchParameterExpressionBase>());

            var orderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).SearchParamTableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(orderedExpressions);
            Assert.Equal(9, orderedExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.All, orderedExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Top, orderedExpressions[1].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)orderedExpressions[2].Predicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceTypes[0]);
            Assert.Equal("prescription", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[3].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[4].Predicate;
            Assert.Equal("MedicationRequest", includeExpression.ResourceTypes[0]);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[5].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[6].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[6].Predicate;
            Assert.Equal("Patient", includeExpression.ResourceTypes[0]);
            Assert.Equal("general-practitioner", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[7].Kind);

            Assert.Equal(SearchParamTableExpressionKind.IncludeUnionAll, orderedExpressions[8].Kind);
        }

        [Fact]
        public void GivenASqlRootExpressionWithMultipleRevIncludes_WhenVisitedByIncludeRewriter_TheExpressionsShouldBeOrderedCorrectly()
        {
            // Order the following query:
            // [base]/Organization?_revinclude:iterate=MedicationDispense:prescription&_revinclude:iterate=MedicationRequest:patient&_revinclude=Patient:organization&_id=organization-id

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "prescription");
            var includeIteratePatientGeneralPractitioner = new IncludeExpression(new[] { "MedicationDispense" }, refSearchParameter, "MedicationDispense", null, null, false, true, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationRequest", "patient");
            var includeIterateMedicationRequestPatient = new IncludeExpression(new[] { "MedicationRequest" }, refSearchParameter, "MedicationRequest", null, null, false, true, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("Patient", "organization");
            var includeMedicationDispensePrescription = new IncludeExpression(new[] { "Patient" }, refSearchParameter, "Patient", null, null, false, true, false);

            Expression predicate = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "Organization", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id", "_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "organization-id", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIteratePatientGeneralPractitioner, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIterateMedicationRequestPatient, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeMedicationDispensePrescription, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
                },
                new List<SearchParameterExpressionBase>());

            var orderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).SearchParamTableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(orderedExpressions);
            Assert.Equal(9, orderedExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.All, orderedExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Top, orderedExpressions[1].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)orderedExpressions[2].Predicate;
            Assert.Equal("Patient", includeExpression.ResourceTypes[0]);
            Assert.Equal("organization", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[3].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[4].Predicate;
            Assert.Equal("MedicationRequest", includeExpression.ResourceTypes[0]);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[5].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[6].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[6].Predicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceTypes[0]);
            Assert.Equal("prescription", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[7].Kind);

            Assert.Equal(SearchParamTableExpressionKind.IncludeUnionAll, orderedExpressions[8].Kind);
        }

        [Fact]
        public void GivenASqlRootExpressionWithMultipleIncludesAndRevIncludes_WhenVisitedByIncludeRewriter_TheExpressionsShouldBeOrderedCorrectly()
        {
            // Order the following query:
            // [base]/Organization?_include:iterate=MedicationDispense:prescription&_revinclude:iterate=MedicationDispense:patient&_revinclude=Patient:organization&_id=organization-id

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "prescription");
            var includeIteratePatientGeneralPractitioner = new IncludeExpression(new[] { "MedicationDispense" }, refSearchParameter, "MedicationDispense", null, null, false, false, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "patient");
            var includeIterateMedicationRequestPatient = new IncludeExpression(new[] { "MedicationDispense" }, refSearchParameter, "MedicationDispense", null, null, false, true, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("Patient", "organization");
            var includeMedicationDispensePrescription = new IncludeExpression(new[] { "Patient" }, refSearchParameter, "Patient", null, null, false, true, false);

            Expression predicate = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "Organization", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id", "_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "organization-id", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIteratePatientGeneralPractitioner, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIterateMedicationRequestPatient, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeMedicationDispensePrescription, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
                },
                new List<SearchParameterExpressionBase>());

            var orderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).SearchParamTableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(orderedExpressions);
            Assert.Equal(9, orderedExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.All, orderedExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Top, orderedExpressions[1].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)orderedExpressions[2].Predicate;
            Assert.Equal("Patient", includeExpression.ResourceTypes[0]);
            Assert.Equal("organization", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[3].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[4].Predicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceTypes[0]);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[5].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[6].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[6].Predicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceTypes[0]);
            Assert.Equal("prescription", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[7].Kind);

            Assert.Equal(SearchParamTableExpressionKind.IncludeUnionAll, orderedExpressions[8].Kind);
        }

        // Queries with search parameters unrelated to the query
        [Fact]
        public void GivenASqlRootExpressionWithParametersUnrelatedToTheQuery_WhenVisitedByIncludeRewriter_TheExpressionsShouldBeOrderedCorrectly()
        {
            // Order the following query:
            // [base]/MedicationDispense?_id=12345&_include:iterate=Device:location&_include:iterate=Location:endpoint&_include=MedicationDispense:performer&_include:iterate=Patient:general-practitioner

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("Device", "location");
            var includeIterateDeviceLocation = new IncludeExpression(new[] { "Device" }, refSearchParameter, "Device", null, null, false, false, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("Location", "endpoint");
            var includeIterateLocationEndpoint = new IncludeExpression(new[] { "Location" }, refSearchParameter, "Location", null, null, false, false, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "performer");
            var includeMedicationDispensePerformer = new IncludeExpression(new[] { "MedicationDispense" }, refSearchParameter, "MedicationDispense", null, null, false, false, false);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("Patient", "general-practitioner");
            var includeIteratePatientPractitioner = new IncludeExpression(new[] { "Patient" }, refSearchParameter, "Patient", null, null, false, false, true);

            Expression predicate = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "Organization", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id", "_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "12345", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIterateDeviceLocation, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIterateLocationEndpoint, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeMedicationDispensePerformer, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIteratePatientPractitioner, SearchParamTableExpressionKind.Include),

                    new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
                },
                new List<SearchParameterExpressionBase>());

            var orderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).SearchParamTableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(orderedExpressions);
            Assert.Equal(11, orderedExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.All, orderedExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Top, orderedExpressions[1].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)orderedExpressions[2].Predicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceTypes[0]);
            Assert.Equal("performer", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[3].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[4].Predicate;
            Assert.Equal("Device", includeExpression.ResourceTypes[0]);
            Assert.Equal("location", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[5].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[6].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[6].Predicate;
            Assert.Equal("Location", includeExpression.ResourceTypes[0]);
            Assert.Equal("endpoint", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[7].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[8].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[8].Predicate;
            Assert.Equal("Patient", includeExpression.ResourceTypes[0]);
            Assert.Equal("general-practitioner", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[9].Kind);

            Assert.Equal(SearchParamTableExpressionKind.IncludeUnionAll, orderedExpressions[10].Kind);
        }

        [Fact]
        public void GivenASqlRootExpressionWithParametersUnrelatedToTheQuerySortedDiferently_WhenVisitedByIncludeRewriter_TheExpressionsShouldBeOrderedCorrectly()
        {
            // Order the following query:
            // [base]/MedicationDispense?_id=12345&_include:iterate=Location:endpoint&_include=MedicationDispense:performer&_include:iterate=Patient:general-practitioner&_include:iterate=Device:location

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("Device", "location");
            var includeIterateDeviceLocation = new IncludeExpression(new[] { "Device" }, refSearchParameter, "Device", null, null, false, false, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("Location", "endpoint");
            var includeIterateLocationEndpoint = new IncludeExpression(new[] { "Location" }, refSearchParameter, "Location", null, null, false, false, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "performer");
            var includeMedicationDispensePerformer = new IncludeExpression(new[] { "MedicationDispense" }, refSearchParameter, "MedicationDispense", null, null, false, false, false);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("Patient", "general-practitioner");
            var includeIteratePatientPractitioner = new IncludeExpression(new[] { "Patient" }, refSearchParameter, "Patient", null, null, false, false, true);

            Expression predicate = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "Organization", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id", "_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "12345", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIterateLocationEndpoint, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeMedicationDispensePerformer, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIteratePatientPractitioner, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIterateDeviceLocation,  SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
                },
                new List<SearchParameterExpressionBase>());

            var orderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).SearchParamTableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(orderedExpressions);
            Assert.Equal(11, orderedExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.All, orderedExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Top, orderedExpressions[1].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)orderedExpressions[2].Predicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceTypes[0]);
            Assert.Equal("performer", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[3].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[4].Predicate;
            Assert.Equal("Patient", includeExpression.ResourceTypes[0]);
            Assert.Equal("general-practitioner", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[9].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[6].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[6].Predicate;
            Assert.Equal("Device", includeExpression.ResourceTypes[0]);
            Assert.Equal("location", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[7].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[8].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[8].Predicate;
            Assert.Equal("Location", includeExpression.ResourceTypes[0]);
            Assert.Equal("endpoint", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[9].Kind);

            Assert.Equal(SearchParamTableExpressionKind.IncludeUnionAll, orderedExpressions[10].Kind);
        }

        // Wildcard Queries

        [Fact]
        public void GivenASqlRootExpressionWithIncludeWildcard_WhenVisitedByIncludeRewriter_TheExpressionsShouldBeOrderedCorrectly()
        {
            // Order the following query:
            // [base]/MedicationDispense?_include:iterate=Patient:general-practitioner&_include:iterate=MedicationRequest:patient&_include=MedicationDispense:*&_id=12345

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("Patient", "general-practitioner");
            var includeIteratePatientGeneralPractitioner = new IncludeExpression(new[] { "Patient" }, refSearchParameter, "Patient", null, null, false, false, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationRequest", "patient");
            var includeIterateMedicationRequestPatient = new IncludeExpression(new[] { "MedicationRequest" }, refSearchParameter, "MedicationRequest", null, null, false, false, true);

            var referencedTypes = new List<string> { "Location", "MedicationRequest", "Patient", "Practitioner", "Organization" }; // partial list of referenced types
            var includeMedicationDispenseWildcard = new IncludeExpression(new[] { "MedicationDispense" }, null, "MedicationDispense", null, referencedTypes, true, false, false);

            Expression predicate = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "MedicationDispense", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id", "_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "12345", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIteratePatientGeneralPractitioner, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIterateMedicationRequestPatient, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeMedicationDispenseWildcard, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
                },
                new List<SearchParameterExpressionBase>());

            var orderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).SearchParamTableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(orderedExpressions);
            Assert.Equal(9, orderedExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.All, orderedExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Top, orderedExpressions[1].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)orderedExpressions[2].Predicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceTypes[0]);
            Assert.True(includeExpression.WildCard);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[3].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[4].Predicate;
            Assert.Equal("MedicationRequest", includeExpression.ResourceTypes[0]);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[5].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[6].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[6].Predicate;
            Assert.Equal("Patient", includeExpression.ResourceTypes[0]);
            Assert.Equal("general-practitioner", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[7].Kind);

            Assert.Equal(SearchParamTableExpressionKind.IncludeUnionAll, orderedExpressions[8].Kind);
        }

        [Fact]
        public void GivenASqlRootExpressionWithRevIncludeWildcard_WhenVisitedByIncludeRewriter_TheExpressionsShouldBeOrderedCorrectly()
        {
            // Order the following query:
            // [base]/MedicationRequest?_include:iterate=MedicationDispense:patient&_revinclude=MedicationDispense:*&_id=12345

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "patient");
            var includeIterateMedicationRequestPatient = new IncludeExpression(new[] { "MedicationDispense" }, refSearchParameter, "MedicationDispense", null, null, false, false, true);

            var referencedTypes = new List<string> { "Location", "MedicationRequest", "Patient", "Practitioner", "Organization" }; // partial list of referenced types
            var revIncludeMedicationDispenseWildcard = new IncludeExpression(new[] { "MedicationDispense" }, null, "MedicationDispense", null, referencedTypes, true, true, false);

            Expression predicate = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "MedicationRequest", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id", "_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "12345", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIterateMedicationRequestPatient, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  revIncludeMedicationDispenseWildcard, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
                },
                new List<SearchParameterExpressionBase>());

            var orderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).SearchParamTableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(orderedExpressions);
            Assert.Equal(7, orderedExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.All, orderedExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Top, orderedExpressions[1].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)orderedExpressions[2].Predicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceTypes[0]);
            Assert.True(includeExpression.WildCard);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[3].Kind);

            Assert.Equal(SearchParamTableExpressionKind.Include, orderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)orderedExpressions[4].Predicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceTypes[0]);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Code);

            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, orderedExpressions[5].Kind);

            Assert.Equal(SearchParamTableExpressionKind.IncludeUnionAll, orderedExpressions[6].Kind);
        }

        [Fact]
        public void GivenASqlRootExpressionWithCyclicIncludeIterate_WhenVisitedByIncludeRewriter_AnErrorIsExpected()
        {
            // Order the following cyclic query:
            // [base]/MedicationDispense?_include=MedicationDispense:prescription&_include:iterate=MedicationRequest:patient&_include:iterate=Patient:general-practitioner&_revinclude:iterate=DiagnosticReport:performer:Practitioner&_include:iterate=DiagnosticReport:patient&_id=12345

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "prescription");
            var includeMedicationDispense = new IncludeExpression(new[] { "MedicationDispense" }, refSearchParameter, "MedicationDispense", null, null, false, false, false);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationRequest", "patient");
            var includeIterateMedicationRequestPatient = new IncludeExpression(new[] { "MedicationRequest" }, refSearchParameter, "MedicationRequest", null, null, false, false, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("Patient", "general-practitioner");
            var includeIteratePatientPractitioner = new IncludeExpression(new[] { "Patient" }, refSearchParameter, "Patient", null, null, false, false, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("DiagnosticReport", "performer");
            var revIncludeIterateDiagnosticReportPerformer = new IncludeExpression(new[] { "DiagnosticReport" }, refSearchParameter, "DiagnosticReport", "Practitioner", null, false, true, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("DiagnosticReport", "patient");
            var includeIterateDiagnosticReportPatient = new IncludeExpression(new[] { "DiagnosticReport" }, refSearchParameter, "DiagnosticReport", null, null, false, false, true);

            Expression predicate = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "MedicationDispense", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id", "_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "12345", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeMedicationDispense, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIterateMedicationRequestPatient, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIteratePatientPractitioner, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  revIncludeIterateDiagnosticReportPerformer, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(IncludeQueryGenerator.Instance,  includeIterateDiagnosticReportPatient, SearchParamTableExpressionKind.Include),
                    new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
                },
                new List<SearchParameterExpressionBase>());

            Assert.Throws<SearchOperationNotSupportedException>(() => ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).SearchParamTableExpressions);
        }

        public class IncludeRewriterFixture
        {
            private bool isInitialized = false;

            public IncludeRewriterFixture()
            {
                IModelInfoProvider modelInfoProvider = MockModelInfoProviderBuilder
                    .Create(FhirSpecification.R4)
                    .AddKnownTypes("Device", "DiagnosticReport", "MedicationRequest", "MedicationDispense", "Location", "Practitioner", "Organization", "Bundle")
                    .Build();
                var mediator = Substitute.For<IMediator>();
                var searchService = Substitute.For<ISearchService>();
                SearchParameterDefinitionManager = new SearchParameterDefinitionManager(modelInfoProvider, mediator, () => searchService.CreateMockScope(), NullLogger<SearchParameterDefinitionManager>.Instance);
            }

            public ISearchParameterDefinitionManager SearchParameterDefinitionManager { get; }

            public async Task Start()
            {
                if (!isInitialized)
                {
                    await ((SearchParameterDefinitionManager)SearchParameterDefinitionManager).StartAsync(CancellationToken.None);
                    await ((SearchParameterDefinitionManager)SearchParameterDefinitionManager).EnsureInitializedAsync(CancellationToken.None);
                    isInitialized = true;
                }
            }
        }
    }
}

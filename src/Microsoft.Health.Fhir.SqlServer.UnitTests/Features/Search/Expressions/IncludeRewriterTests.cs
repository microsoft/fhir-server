// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    public class IncludeRewriterTests
    {
        private readonly SearchParameterDefinitionManager _searchParameterDefinitionManager;
        private IReadOnlyList<string> _includeTargetTypes = new List<string>() { "MedicationRequest" };

        public IncludeRewriterTests()
        {
            ModelInfoProvider.SetProvider(new VersionSpecificModelInfoProvider());
            _searchParameterDefinitionManager = new SearchParameterDefinitionManager(ModelInfoProvider.Instance);
            _searchParameterDefinitionManager.Start();
        }

        // Basic Queries with 0-2 include search parameters with all the pair combinations

        [Fact]
        public void GivenASqlRootExpressionWithoutIncludes_WhenVisitedByIncludeRewriter_TheSameExpressionShouldBeReturnedAsIs()
        {
            // Leave the query as is if there's no Include expression. For example:
            // [base]/Patient?gender=female&family=Ellison

            Expression denormalizedExpression = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "Patient", false)),
                    new SearchParameterExpression(new SearchParameterInfo("gender"), new StringExpression(StringOperator.Equals, FieldName.String, null, "female", false)),
                    new SearchParameterExpression(new SearchParameterInfo("family"), new StringExpression(StringOperator.Equals, FieldName.String, null, "Ellison", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<TableExpression>
                {
                    new TableExpression(null, null, denormalizedExpression, TableExpressionKind.All),
                    new TableExpression(null, null, null, TableExpressionKind.Top),
                },
                new List<Expression>());

            var rewrittenExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).TableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(rewrittenExpressions);
            Assert.Equal(2, rewrittenExpressions.Count);

            Assert.Equal(TableExpressionKind.All, rewrittenExpressions[0].Kind);
            Assert.Equal(TableExpressionKind.Top, rewrittenExpressions[1].Kind);
        }

        [Fact]
        public void GivenASqlRootExpressionWithTwoIncludes_WhenVisitedByIncludeRewriter_TheOrderDoesNotMatterAndShouldRemainUnchanged()
        {
            // Order the following query:
            // [base]/MedicationDispense?_include=MedicationDispense:prescription&_include=MedicationDispense:patient&_id=smart-MedicationDispense-567

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "prescription");
            var includeMedicationDispensePrescription = new IncludeExpression("MedicationDispense", refSearchParameter, "MedicationDispense", null, null, false, false, false);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "patient");
            var includeMedicationDispensePatient = new IncludeExpression("MedicationDispense", refSearchParameter, "MedicationDispense", null, null, false, false, false);

            Expression denormalizedExpression = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "MedicationDispense", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "smart-MedicationDispense-567", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<TableExpression>
                {
                    new TableExpression(null, null, denormalizedExpression, TableExpressionKind.All),
                    new TableExpression(IncludeQueryGenerator.Instance,  includeMedicationDispensePrescription, null, TableExpressionKind.Include),
                    new TableExpression(IncludeQueryGenerator.Instance,  includeMedicationDispensePatient, null, TableExpressionKind.Include),
                    new TableExpression(null, null, null, TableExpressionKind.Top),
                },
                new List<Expression>());

            var reorderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).TableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(reorderedExpressions);
            Assert.Equal(7, reorderedExpressions.Count);

            Assert.Equal(TableExpressionKind.All, reorderedExpressions[0].Kind);
            Assert.Equal(TableExpressionKind.Top, reorderedExpressions[1].Kind);

            Assert.Equal(TableExpressionKind.Include, reorderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)reorderedExpressions[2].NormalizedPredicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceType);
            Assert.Equal("prescription", includeExpression.ReferenceSearchParameter.Name);

            Assert.Equal(TableExpressionKind.IncludeLimit, reorderedExpressions[3].Kind);

            Assert.Equal(TableExpressionKind.Include, reorderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)reorderedExpressions[4].NormalizedPredicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceType);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Name);

            Assert.Equal(TableExpressionKind.IncludeLimit, reorderedExpressions[5].Kind);

            Assert.Equal(TableExpressionKind.IncludeUnionAll, reorderedExpressions[6].Kind);
        }

        [Fact]
        public void GivenASqlRootExpressionWithOneIncludeAndOneIncludeIterate_WhenVisitedByIncludeRewriter_TheExpressionsShouldBeOrderedCorrectly()
        {
            // Order the following query:
            // [base]/MedicationDispense?_include:iterate=Patient:general-practitioner&_include=MedicationRequest:patient&_id=12345

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("Patient", "general-practitioner");
            var includeIteratePatientGeneralPractitioner = new IncludeExpression("Patient", refSearchParameter, "Patient", null, null, false, false, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "patient");
            var includeMedicationDispensePatient = new IncludeExpression("MedicationDispense", refSearchParameter, "MedicationDispense", null, null, false, false, false);

            Expression denormalizedExpression = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "MedicationDispense", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "12345", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<TableExpression>
                {
                    new TableExpression(null, null, denormalizedExpression, TableExpressionKind.All),
                    new TableExpression(IncludeQueryGenerator.Instance,  includeIteratePatientGeneralPractitioner, null, TableExpressionKind.Include),
                    new TableExpression(IncludeQueryGenerator.Instance,  includeMedicationDispensePatient, null, TableExpressionKind.Include),
                    new TableExpression(null, null, null, TableExpressionKind.Top),
                },
                new List<Expression>());

            var reorderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).TableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(reorderedExpressions);
            Assert.Equal(7, reorderedExpressions.Count);

            Assert.Equal(TableExpressionKind.All, reorderedExpressions[0].Kind);
            Assert.Equal(TableExpressionKind.Top, reorderedExpressions[1].Kind);

            Assert.Equal(TableExpressionKind.Include, reorderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)reorderedExpressions[2].NormalizedPredicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceType);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Name);

            Assert.Equal(TableExpressionKind.IncludeLimit, reorderedExpressions[3].Kind);

            Assert.Equal(TableExpressionKind.Include, reorderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)reorderedExpressions[4].NormalizedPredicate;
            Assert.Equal("Patient", includeExpression.ResourceType);
            Assert.Equal("general-practitioner", includeExpression.ReferenceSearchParameter.Name);

            Assert.Equal(TableExpressionKind.IncludeLimit, reorderedExpressions[5].Kind);

            Assert.Equal(TableExpressionKind.IncludeUnionAll, reorderedExpressions[6].Kind);
        }

        [Fact]
        public void GivenASqlRootExpressionWithOneIncludeAndOneRevInclude_WhenVisitedByIncludeRewriter_TheOrderDoesNotMatterAndShouldRemainUnchanged()
        {
            // Order the following query:
            // [base]/MedicationRequest?_include=MedicationRequest:patient&_revinclude=MedicationDispense:prescription&_id=12345

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationRequest", "patient");
            var includeMedicationRequestPatient = new IncludeExpression("MedicationRequest", refSearchParameter, "MedicationRequest", null, null, false, false, false);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "prescription");
            var revincludeMedicationDispensePrescription = new IncludeExpression("MedicationDispense", refSearchParameter, "MedicationDispense", null, null, false, true, false);

            Expression denormalizedExpression = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "MedicationRequest", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "12345", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<TableExpression>
                {
                    new TableExpression(null, null, denormalizedExpression, TableExpressionKind.All),
                    new TableExpression(IncludeQueryGenerator.Instance,  includeMedicationRequestPatient, null, TableExpressionKind.Include),
                    new TableExpression(IncludeQueryGenerator.Instance,  revincludeMedicationDispensePrescription, null, TableExpressionKind.Include),
                    new TableExpression(null, null, null, TableExpressionKind.Top),
                },
                new List<Expression>());

            var reorderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).TableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(reorderedExpressions);
            Assert.Equal(7, reorderedExpressions.Count);

            Assert.Equal(TableExpressionKind.All, reorderedExpressions[0].Kind);
            Assert.Equal(TableExpressionKind.Top, reorderedExpressions[1].Kind);

            Assert.Equal(TableExpressionKind.Include, reorderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)reorderedExpressions[2].NormalizedPredicate;
            Assert.Equal("MedicationRequest", includeExpression.ResourceType);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Name);

            Assert.Equal(TableExpressionKind.IncludeLimit, reorderedExpressions[3].Kind);

            Assert.Equal(TableExpressionKind.Include, reorderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)reorderedExpressions[4].NormalizedPredicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceType);
            Assert.Equal("prescription", includeExpression.ReferenceSearchParameter.Name);

            Assert.Equal(TableExpressionKind.IncludeLimit, reorderedExpressions[5].Kind);

            Assert.Equal(TableExpressionKind.IncludeUnionAll, reorderedExpressions[6].Kind);
        }

        [Fact]
        public void GivenASqlRootExpressionWithOneIncludeAndOneRevIncludeIterate_WhenVisitedByIncludeRewriter_TheExpressionsShouldBeOrderedCorrectly()
        {
            // Order the following query:
            // [base]/MedicationRequest?_revinclude:iterate=MedicationDispense:patient&_include=MedicationRequest:patient&_id=12345

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "patient");
            var revincludeIterateMedicationDispensePatient = new IncludeExpression("MedicationDispense", refSearchParameter, "MedicationDispense", null, null, false, true, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationRequest", "patient");
            var includeMedicationRequestPatient = new IncludeExpression("MedicationRequest", refSearchParameter, "MedicationRequest", null, null, false, false, false);

            Expression denormalizedExpression = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "MedicationRequest", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "12345", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<TableExpression>
                {
                    new TableExpression(null, null, denormalizedExpression, TableExpressionKind.All),
                    new TableExpression(IncludeQueryGenerator.Instance,  revincludeIterateMedicationDispensePatient, null, TableExpressionKind.Include),
                    new TableExpression(IncludeQueryGenerator.Instance,  includeMedicationRequestPatient, null, TableExpressionKind.Include),
                    new TableExpression(null, null, null, TableExpressionKind.Top),
                },
                new List<Expression>());

            var reorderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).TableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(reorderedExpressions);
            Assert.Equal(7, reorderedExpressions.Count);

            Assert.Equal(TableExpressionKind.All, reorderedExpressions[0].Kind);
            Assert.Equal(TableExpressionKind.Top, reorderedExpressions[1].Kind);

            Assert.Equal(TableExpressionKind.Include, reorderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)reorderedExpressions[2].NormalizedPredicate;
            Assert.Equal("MedicationRequest", includeExpression.ResourceType);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Name);

            Assert.Equal(TableExpressionKind.IncludeLimit, reorderedExpressions[3].Kind);

            Assert.Equal(TableExpressionKind.Include, reorderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)reorderedExpressions[4].NormalizedPredicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceType);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Name);

            Assert.Equal(TableExpressionKind.IncludeLimit, reorderedExpressions[5].Kind);

            Assert.Equal(TableExpressionKind.IncludeUnionAll, reorderedExpressions[6].Kind);
        }

        [Fact]
        public void GivenASqlRootExpressionWithTwoRevIncludes_WhenVisitedByIncludeRewriter_TheOrderDoesNotMatterAndShouldRemainUnchanged()
        {
            // Order the following query:
            // [base]/Patient?_revinclude=MedicationDispense:patient&_revinclude=MedicationRequest:patient&_id=patientId

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "patient");
            var revincludeMedicationDispensePatient = new IncludeExpression("MedicationDispense", refSearchParameter, "MedicationDispense", null, null, false, true, false);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationRequest", "patient");
            var revincludeMedicationRequestPatient = new IncludeExpression("MedicationRequest", refSearchParameter, "MedicationRequest", null, null, false, true, false);

            Expression denormalizedExpression = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "Patient", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "patientId", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<TableExpression>
                {
                    new TableExpression(null, null, denormalizedExpression, TableExpressionKind.All),
                    new TableExpression(IncludeQueryGenerator.Instance,  revincludeMedicationDispensePatient, null, TableExpressionKind.Include),
                    new TableExpression(IncludeQueryGenerator.Instance,  revincludeMedicationRequestPatient, null, TableExpressionKind.Include),
                    new TableExpression(null, null, null, TableExpressionKind.Top),
                },
                new List<Expression>());

            var reorderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).TableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(reorderedExpressions);
            Assert.Equal(7, reorderedExpressions.Count);

            Assert.Equal(TableExpressionKind.All, reorderedExpressions[0].Kind);
            Assert.Equal(TableExpressionKind.Top, reorderedExpressions[1].Kind);

            Assert.Equal(TableExpressionKind.Include, reorderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)reorderedExpressions[2].NormalizedPredicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceType);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Name);

            Assert.Equal(TableExpressionKind.IncludeLimit, reorderedExpressions[3].Kind);

            Assert.Equal(TableExpressionKind.Include, reorderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)reorderedExpressions[4].NormalizedPredicate;
            Assert.Equal("MedicationRequest", includeExpression.ResourceType);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Name);

            Assert.Equal(TableExpressionKind.IncludeLimit, reorderedExpressions[5].Kind);

            Assert.Equal(TableExpressionKind.IncludeUnionAll, reorderedExpressions[6].Kind);
        }

        [Fact]
        public void GivenASqlRootExpressionWithOneRevIncludeAndOneRevIncludeIterate_WhenVisitedByIncludeRewriter_TheExpressionsShouldBeOrderedCorrectly()
        {
            // Order the following query:
            // [base]/Practitioner?_revinclude:iterate=MedicationRequest:patient&_revinclude=Patient:general-practitioner&_id=12345

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationRequest", "patient");
            var revincludeIterateMedicationRequestPatient = new IncludeExpression("MedicationRequest", refSearchParameter, "MedicationRequest", null, null, false, true, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("Patient", "general-practitioner");
            var revincludePatientGeneralPractitioner = new IncludeExpression("Patient", refSearchParameter, "Patient", null, null, false, true, false);

            Expression denormalizedExpression = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "Practitioner", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "12345", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<TableExpression>
                {
                    new TableExpression(null, null, denormalizedExpression, TableExpressionKind.All),
                    new TableExpression(IncludeQueryGenerator.Instance,  revincludeIterateMedicationRequestPatient, null, TableExpressionKind.Include),
                    new TableExpression(IncludeQueryGenerator.Instance,  revincludePatientGeneralPractitioner, null, TableExpressionKind.Include),
                    new TableExpression(null, null, null, TableExpressionKind.Top),
                },
                new List<Expression>());

            var reorderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).TableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(reorderedExpressions);
            Assert.Equal(7, reorderedExpressions.Count);

            Assert.Equal(TableExpressionKind.All, reorderedExpressions[0].Kind);
            Assert.Equal(TableExpressionKind.Top, reorderedExpressions[1].Kind);

            Assert.Equal(TableExpressionKind.Include, reorderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)reorderedExpressions[2].NormalizedPredicate;
            Assert.Equal("Patient", includeExpression.ResourceType);
            Assert.Equal("general-practitioner", includeExpression.ReferenceSearchParameter.Name);

            Assert.Equal(TableExpressionKind.IncludeLimit, reorderedExpressions[3].Kind);

            Assert.Equal(TableExpressionKind.Include, reorderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)reorderedExpressions[4].NormalizedPredicate;
            Assert.Equal("MedicationRequest", includeExpression.ResourceType);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Name);

            Assert.Equal(TableExpressionKind.IncludeLimit, reorderedExpressions[5].Kind);

            Assert.Equal(TableExpressionKind.IncludeUnionAll, reorderedExpressions[6].Kind);
        }

        [Fact]
        public void GivenASqlRootExpressionWithOneRevIncludeAndOneIncludeIterate_WhenVisitedByIncludeRewriter_TheExpressionsShouldBeOrderedCorrectly()
        {
            // Order the following query:
            // [base]/MedicationRequest?_include:iterate=MedicationRequest:patient&_revinclude=MedicationDispense:prescription&_id=12345

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationRequest", "patient");
            var includeIterateMedicationRequestPatient = new IncludeExpression("MedicationRequest", refSearchParameter, "MedicationRequest", null, null, false, false, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "prescription");
            var revincludeMedicationDispensePrescription = new IncludeExpression("MedicationDispense", refSearchParameter, "MedicationDispense", null, null, false, true, false);

            Expression denormalizedExpression = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "MedicationRequest", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "12345", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<TableExpression>
                {
                    new TableExpression(null, null, denormalizedExpression, TableExpressionKind.All),
                    new TableExpression(IncludeQueryGenerator.Instance,  includeIterateMedicationRequestPatient, null, TableExpressionKind.Include),
                    new TableExpression(IncludeQueryGenerator.Instance,  revincludeMedicationDispensePrescription, null, TableExpressionKind.Include),
                    new TableExpression(null, null, null, TableExpressionKind.Top),
                },
                new List<Expression>());

            var reorderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).TableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(reorderedExpressions);
            Assert.Equal(7, reorderedExpressions.Count);

            Assert.Equal(TableExpressionKind.All, reorderedExpressions[0].Kind);
            Assert.Equal(TableExpressionKind.Top, reorderedExpressions[1].Kind);

            Assert.Equal(TableExpressionKind.Include, reorderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)reorderedExpressions[2].NormalizedPredicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceType);
            Assert.Equal("prescription", includeExpression.ReferenceSearchParameter.Name);

            Assert.Equal(TableExpressionKind.IncludeLimit, reorderedExpressions[3].Kind);

            Assert.Equal(TableExpressionKind.Include, reorderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)reorderedExpressions[4].NormalizedPredicate;
            Assert.Equal("MedicationRequest", includeExpression.ResourceType);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Name);

            Assert.Equal(TableExpressionKind.IncludeLimit, reorderedExpressions[5].Kind);

            Assert.Equal(TableExpressionKind.IncludeUnionAll, reorderedExpressions[6].Kind);
        }

        // Queries with multiple includes/revincludes

        [Fact]
        public void GivenASqlRootExpressionWithMultipleIncludes_WhenVisitedByIncludeRewriter_TheExpressionsShouldBeOrderedCorrectly()
        {
            // Order the following query:
            // [base]/MedicationDispense?_include:iterate=Patient:general-practitioner&_include:iterate=MedicationRequest:patient&_include=MedicationDispense:prescription&_id=smart-MedicationDispense-567

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("Patient", "general-practitioner");
            var includeIteratePatientGeneralPractitioner = new IncludeExpression("Patient", refSearchParameter, "Patient", null, null, false, false, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationRequest", "patient");
            var includeIterateMedicationRequestPatient = new IncludeExpression("MedicationRequest", refSearchParameter, "MedicationRequest", null, null, false, false, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "prescription");
            var includeMedicationDispensePrescription = new IncludeExpression("MedicationDispense", refSearchParameter, "MedicationDispense", null, null, false, false, false);

            Expression denormalizedExpression = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "MedicationDispense", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "smart-MedicationDispense-567", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<TableExpression>
                {
                    new TableExpression(null, null, denormalizedExpression, TableExpressionKind.All),
                    new TableExpression(IncludeQueryGenerator.Instance,  includeIteratePatientGeneralPractitioner, null, TableExpressionKind.Include),
                    new TableExpression(IncludeQueryGenerator.Instance,  includeIterateMedicationRequestPatient, null, TableExpressionKind.Include),
                    new TableExpression(IncludeQueryGenerator.Instance,  includeMedicationDispensePrescription, null, TableExpressionKind.Include),
                    new TableExpression(null, null, null, TableExpressionKind.Top),
                },
                new List<Expression>());

            var reorderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).TableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(reorderedExpressions);
            Assert.Equal(9, reorderedExpressions.Count);

            Assert.Equal(TableExpressionKind.All, reorderedExpressions[0].Kind);
            Assert.Equal(TableExpressionKind.Top, reorderedExpressions[1].Kind);

            Assert.Equal(TableExpressionKind.Include, reorderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)reorderedExpressions[2].NormalizedPredicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceType);
            Assert.Equal("prescription", includeExpression.ReferenceSearchParameter.Name);

            Assert.Equal(TableExpressionKind.IncludeLimit, reorderedExpressions[3].Kind);

            Assert.Equal(TableExpressionKind.Include, reorderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)reorderedExpressions[4].NormalizedPredicate;
            Assert.Equal("MedicationRequest", includeExpression.ResourceType);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Name);

            Assert.Equal(TableExpressionKind.IncludeLimit, reorderedExpressions[5].Kind);

            Assert.Equal(TableExpressionKind.Include, reorderedExpressions[6].Kind);
            includeExpression = (IncludeExpression)reorderedExpressions[6].NormalizedPredicate;
            Assert.Equal("Patient", includeExpression.ResourceType);
            Assert.Equal("general-practitioner", includeExpression.ReferenceSearchParameter.Name);

            Assert.Equal(TableExpressionKind.IncludeLimit, reorderedExpressions[7].Kind);

            Assert.Equal(TableExpressionKind.IncludeUnionAll, reorderedExpressions[8].Kind);
        }

        [Fact]
        public void GivenASqlRootExpressionWithMultipleRevIncludes_WhenVisitedByIncludeRewriter_TheExpressionsShouldBeOrderedCorrectly()
        {
            // Order the following query:
            // [base]/Organization?_revinclude:iterate=MedicationDispense:prescription&_revinclude:iterate=MedicationRequest:patient&_revinclude=Patient:organization&_id=organization-id

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "prescription");
            var includeIteratePatientGeneralPractitioner = new IncludeExpression("MedicationDispense", refSearchParameter, "MedicationDispense", null, null, false, true, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationRequest", "patient");
            var includeIterateMedicationRequestPatient = new IncludeExpression("MedicationRequest", refSearchParameter, "MedicationRequest", null, null, false, true, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("Patient", "organization");
            var includeMedicationDispensePrescription = new IncludeExpression("Patient", refSearchParameter, "Patient", null, null, false, true, false);

            Expression denormalizedExpression = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "Organization", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "organization-id", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<TableExpression>
                {
                    new TableExpression(null, null, denormalizedExpression, TableExpressionKind.All),
                    new TableExpression(IncludeQueryGenerator.Instance,  includeIteratePatientGeneralPractitioner, null, TableExpressionKind.Include),
                    new TableExpression(IncludeQueryGenerator.Instance,  includeIterateMedicationRequestPatient, null, TableExpressionKind.Include),
                    new TableExpression(IncludeQueryGenerator.Instance,  includeMedicationDispensePrescription, null, TableExpressionKind.Include),
                    new TableExpression(null, null, null, TableExpressionKind.Top),
                },
                new List<Expression>());

            var reorderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).TableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(reorderedExpressions);
            Assert.Equal(9, reorderedExpressions.Count);

            Assert.Equal(TableExpressionKind.All, reorderedExpressions[0].Kind);
            Assert.Equal(TableExpressionKind.Top, reorderedExpressions[1].Kind);

            Assert.Equal(TableExpressionKind.Include, reorderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)reorderedExpressions[2].NormalizedPredicate;
            Assert.Equal("Patient", includeExpression.ResourceType);
            Assert.Equal("organization", includeExpression.ReferenceSearchParameter.Name);

            Assert.Equal(TableExpressionKind.IncludeLimit, reorderedExpressions[3].Kind);

            Assert.Equal(TableExpressionKind.Include, reorderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)reorderedExpressions[4].NormalizedPredicate;
            Assert.Equal("MedicationRequest", includeExpression.ResourceType);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Name);

            Assert.Equal(TableExpressionKind.IncludeLimit, reorderedExpressions[5].Kind);

            Assert.Equal(TableExpressionKind.Include, reorderedExpressions[6].Kind);
            includeExpression = (IncludeExpression)reorderedExpressions[6].NormalizedPredicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceType);
            Assert.Equal("prescription", includeExpression.ReferenceSearchParameter.Name);

            Assert.Equal(TableExpressionKind.IncludeLimit, reorderedExpressions[7].Kind);

            Assert.Equal(TableExpressionKind.IncludeUnionAll, reorderedExpressions[8].Kind);
        }

        [Fact]
        public void GivenASqlRootExpressionWithMultipleIncludesAndRevIncludes_WhenVisitedByIncludeRewriter_OTheExpressionsShouldBeOrderedCorrectly()
        {
            // Order the following query:
            // [base]/Organization?_include:iterate=MedicationDispense:prescription&_revinclude:iterate=MedicationDispense:patient&_revinclude=Patient:organization&_id=organization-id

            var refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "prescription");
            var includeIteratePatientGeneralPractitioner = new IncludeExpression("MedicationDispense", refSearchParameter, "MedicationDispense", null, null, false, false, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("MedicationDispense", "patient");
            var includeIterateMedicationRequestPatient = new IncludeExpression("MedicationDispense", refSearchParameter, "MedicationDispense", null, null, false, true, true);

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter("Patient", "organization");
            var includeMedicationDispensePrescription = new IncludeExpression("Patient", refSearchParameter, "Patient", null, null, false, true, false);

            Expression denormalizedExpression = Expression.And(new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo("_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "Organization", false)),
                    new SearchParameterExpression(new SearchParameterInfo("_id"), new StringExpression(StringOperator.Equals, FieldName.String, null, "organization-id", false)),
                });

            var sqlExpression = new SqlRootExpression(
                new List<TableExpression>
                {
                    new TableExpression(null, null, denormalizedExpression, TableExpressionKind.All),
                    new TableExpression(IncludeQueryGenerator.Instance,  includeIteratePatientGeneralPractitioner, null, TableExpressionKind.Include),
                    new TableExpression(IncludeQueryGenerator.Instance,  includeIterateMedicationRequestPatient, null, TableExpressionKind.Include),
                    new TableExpression(IncludeQueryGenerator.Instance,  includeMedicationDispensePrescription, null, TableExpressionKind.Include),
                    new TableExpression(null, null, null, TableExpressionKind.Top),
                },
                new List<Expression>());

            var reorderedExpressions = ((SqlRootExpression)sqlExpression.AcceptVisitor(IncludeRewriter.Instance)).TableExpressions;

            // Assert the number of expressions and their order is correct, including IncludeUnionAll expression, which was added in the IncludeRewriter visit.
            Assert.NotNull(reorderedExpressions);
            Assert.Equal(9, reorderedExpressions.Count);

            Assert.Equal(TableExpressionKind.All, reorderedExpressions[0].Kind);
            Assert.Equal(TableExpressionKind.Top, reorderedExpressions[1].Kind);

            Assert.Equal(TableExpressionKind.Include, reorderedExpressions[2].Kind);
            var includeExpression = (IncludeExpression)reorderedExpressions[2].NormalizedPredicate;
            Assert.Equal("Patient", includeExpression.ResourceType);
            Assert.Equal("organization", includeExpression.ReferenceSearchParameter.Name);

            Assert.Equal(TableExpressionKind.IncludeLimit, reorderedExpressions[3].Kind);

            Assert.Equal(TableExpressionKind.Include, reorderedExpressions[4].Kind);
            includeExpression = (IncludeExpression)reorderedExpressions[4].NormalizedPredicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceType);
            Assert.Equal("patient", includeExpression.ReferenceSearchParameter.Name);

            Assert.Equal(TableExpressionKind.IncludeLimit, reorderedExpressions[5].Kind);

            Assert.Equal(TableExpressionKind.Include, reorderedExpressions[6].Kind);
            includeExpression = (IncludeExpression)reorderedExpressions[6].NormalizedPredicate;
            Assert.Equal("MedicationDispense", includeExpression.ResourceType);
            Assert.Equal("prescription", includeExpression.ReferenceSearchParameter.Name);

            Assert.Equal(TableExpressionKind.IncludeLimit, reorderedExpressions[7].Kind);

            Assert.Equal(TableExpressionKind.IncludeUnionAll, reorderedExpressions[8].Kind);
        }
    }
}
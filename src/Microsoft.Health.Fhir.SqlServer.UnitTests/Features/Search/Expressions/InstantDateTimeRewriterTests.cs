// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Registration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class InstantDateTimeRewriterTests
    {
        private readonly InstantDateTimeRewriter _rewriter = new InstantDateTimeRewriter();
        private static readonly DateTimeOffset Boundary = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

        [Fact]
        public void GivenDefaultConfiguration_WhenCreated_ThenInstantRewritingIsDisabled()
        {
            Assert.False(new FhirSqlServerConfiguration().EnableInstantDateTimeRewriter);
        }

        [Theory]
        [InlineData(BinaryOperator.Equal, null)]
        [InlineData(BinaryOperator.NotEqual, 0)]
        [InlineData(BinaryOperator.GreaterThan, null)]
        [InlineData(BinaryOperator.GreaterThanOrEqual, 2)]
        [InlineData(BinaryOperator.LessThan, null)]
        [InlineData(BinaryOperator.LessThanOrEqual, null)]
        public void GivenSlotStartPredicate_WhenRewritten_ThenOnlyTheFieldChanges(BinaryOperator operation, int? component)
        {
            object value = Boundary;
            var original = Expression.SearchParameter(Parameter(), new BinaryExpression(operation, FieldName.DateTimeEnd, component, value));

            var result = Assert.IsType<SearchParameterExpression>(original.AcceptVisitor(_rewriter));

            var binary = Assert.IsType<BinaryExpression>(result.Expression);
            Assert.Same(original.Parameter, result.Parameter);
            Assert.Equal(FieldName.DateTimeStart, binary.FieldName);
            Assert.Equal(operation, binary.BinaryOperator);
            Assert.Equal(component, binary.ComponentIndex);
            Assert.Same(value, binary.Value);
            Assert.Same(result, result.AcceptVisitor(_rewriter));
        }

        [Fact]
        public void GivenHomogeneousInstantUnion_WhenRewritten_ThenExpressionIsUnchanged()
        {
            var original = Expression.SearchParameter(Parameter("Slot.start | Slot.end"), EndPredicate());

            Assert.Same(original, original.AcceptVisitor(_rewriter));
        }

        [Theory]
        [InlineData("Patient.birthDate", TemporalType.Date)]
        [InlineData("Composition.date", TemporalType.DateTime)]
        [InlineData("Schedule.planningHorizon", TemporalType.Period)]
        [InlineData("MedicationRequest.dosageInstruction.timing", TemporalType.Timing)]
        [InlineData("Slot.start | Composition.date", TemporalType.Mixed)]
        public void GivenNonInstantFhirTemporalType_WhenRewritten_ThenExpressionIsUnchanged(
            string path,
            TemporalType temporalType)
        {
            // Type proof is covered by classifier and bundled-definition tests; this tests SQL policy.
            var parameter = new SearchParameterInfo(
                "date",
                "date",
                SearchParamType.Date,
                expression: path)
            {
                IsSystemDefined = true,
                TemporalType = temporalType,
            };
            var original = Expression.SearchParameter(parameter, EndPredicate());

            Assert.Same(original, original.AcceptVisitor(_rewriter));
        }

        [Fact]
        public void GivenUnclassifiedSystemParameter_WhenRewritten_ThenExpressionIsUnchanged()
        {
            var parameter = new SearchParameterInfo("start", "start", SearchParamType.Date, expression: "Slot.start")
            {
                IsSystemDefined = true,
            };
            var original = Expression.SearchParameter(parameter, EndPredicate());

            Assert.Same(original, original.AcceptVisitor(_rewriter));
        }

        [Fact]
        public void GivenCustomOrCompositeOrNonDateParameter_WhenRewritten_ThenExpressionIsUnchanged()
        {
            // Only the custom definition is registrable. The altered built-in definitions test invalid metadata rejection.
            var parameters = new[]
            {
                new SearchParameterInfo(
                    "custom-start",
                    "custom-start",
                    SearchParamType.Date,
                    new Uri("http://example.org/SearchParameter/custom-start"),
                    expression: "Slot.start") { TemporalType = TemporalType.Instant },
                Parameter(type: SearchParamType.Composite),
                Parameter(type: SearchParamType.String),
            };
            foreach (SearchParameterInfo parameter in parameters)
            {
                var original = Expression.SearchParameter(parameter, EndPredicate());
                Assert.Same(original, original.AcceptVisitor(_rewriter));
            }
        }

        [Fact]
        public void GivenNestedAndNeighboringParameters_WhenRewritten_ThenEligibilityDoesNotLeak()
        {
            var fallback = Expression.SearchParameter(ScheduleDateParameter(), EndPredicate());
            var missing = Expression.MissingSearchParameter(Parameter(), true);
            var nonDate = Expression.Equals(FieldName.TokenCode, null, "code");
            var original = Expression.And(
                Expression.SearchParameter(Parameter(), Expression.Or(EndPredicate(), Expression.Not(fallback), nonDate)),
                fallback,
                missing);

            var result = Assert.IsType<MultiaryExpression>(original.AcceptVisitor(_rewriter));

            var payload = Assert.IsType<MultiaryExpression>(Assert.IsType<SearchParameterExpression>(result.Expressions[0]).Expression);
            Assert.Equal(FieldName.DateTimeStart, Assert.IsType<BinaryExpression>(payload.Expressions[0]).FieldName);
            Assert.Same(fallback, Assert.IsType<NotExpression>(payload.Expressions[1]).Expression);
            Assert.Same(nonDate, payload.Expressions[2]);
            Assert.Same(fallback, result.Expressions[1]);
            Assert.Same(missing, result.Expressions[2]);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void GivenChainTable_WhenRewritten_ThenAllTableAndChainMetadataIsPreserved(bool reversed)
        {
            var slot = Expression.SearchParameter(Parameter(), EndPredicate());
            var schedule = Expression.SearchParameter(ScheduleDateParameter(), EndPredicate());
            var reference = new SearchParameterInfo(
                "schedule",
                "schedule",
                SearchParamType.Reference,
                new Uri("http://hl7.org/fhir/SearchParameter/Slot-schedule"),
                expression: "Slot.schedule");
            var source = reversed ? schedule : slot;
            var target = reversed ? slot : schedule;
            var chain = new SqlChainLinkExpression(
                new[] { "Slot" },
                reference,
                new[] { "Schedule" },
                reversed,
                source,
                target);
            var generator = target.AcceptVisitor(new SearchParamTableExpressionQueryGeneratorFactory(new SearchParameterToSearchValueTypeMap()));
            var table = new SearchParamTableExpression(generator, chain, SearchParamTableExpressionKind.Chain, 7);

            var result = Assert.IsType<SearchParamTableExpression>(table.AcceptVisitor(_rewriter));

            Assert.Same(generator, result.QueryGenerator);
            Assert.Equal(table.Kind, result.Kind);
            Assert.Equal(7, result.ChainLevel);
            var resultChain = Assert.IsType<SqlChainLinkExpression>(result.Predicate);
            Assert.Same(chain.ResourceTypes, resultChain.ResourceTypes);
            Assert.Same(chain.TargetResourceTypes, resultChain.TargetResourceTypes);
            Assert.Same(reference, resultChain.ReferenceSearchParameter);
            Assert.Equal(reversed, resultChain.Reversed);
            Assert.Same(schedule, reversed ? resultChain.ExpressionOnSource : resultChain.ExpressionOnTarget);
            var slotPredicate = Assert.IsType<SearchParameterExpression>(reversed ? resultChain.ExpressionOnTarget : resultChain.ExpressionOnSource);
            Assert.Equal(FieldName.DateTimeStart, Assert.IsType<BinaryExpression>(slotPredicate.Expression).FieldName);
        }

        [Fact]
        public void GivenUnionBranches_WhenRewritten_ThenEachParameterNeedsItsOwnProof()
        {
            var instant = Expression.SearchParameter(Parameter(), EndPredicate());
            var fallback = Expression.SearchParameter(ScheduleDateParameter(), EndPredicate());
            var original = new UnionExpression(UnionOperator.All, new[] { instant, fallback });

            var result = Assert.IsType<UnionExpression>(original.AcceptVisitor(_rewriter));

            Assert.Equal(original.Operator, result.Operator);
            Assert.Same(fallback, result.Expressions[1]);
            var rewritten = Assert.IsType<SearchParameterExpression>(result.Expressions[0]);
            Assert.Equal(FieldName.DateTimeStart, Assert.IsType<BinaryExpression>(rewritten.Expression).FieldName);
        }

        private static SearchParameterInfo Parameter(string path = "Slot.start", SearchParamType type = SearchParamType.Date) =>
            new SearchParameterInfo("start", "start", type, new Uri("http://hl7.org/fhir/SearchParameter/Slot-start"), expression: path)
            {
                IsSystemDefined = true,
                TemporalType = TemporalType.Instant,
            };

        private static SearchParameterInfo ScheduleDateParameter() =>
            new SearchParameterInfo(
                "date",
                "date",
                SearchParamType.Date,
                new Uri("http://hl7.org/fhir/SearchParameter/Schedule-date"),
                expression: "Schedule.planningHorizon",
                baseResourceTypes: new[] { "Schedule" })
            {
                IsSystemDefined = true,
                TemporalType = TemporalType.Period,
            };

        private static BinaryExpression EndPredicate() => Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, Boundary);
    }
}

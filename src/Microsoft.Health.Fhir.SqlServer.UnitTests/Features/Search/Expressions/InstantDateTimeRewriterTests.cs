// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Specification;
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
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class InstantDateTimeRewriterTests
    {
        private readonly IModelInfoProvider _model = Substitute.For<IModelInfoProvider>();
        private readonly InstantDateTimeRewriter _rewriter;
        private static readonly DateTimeOffset Boundary = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

        public InstantDateTimeRewriterTests()
        {
            _model.IsKnownResource("Slot", false).Returns(true);
            _model.IsKnownResource("Schedule", false).Returns(true);
            var summary = Summary(Element("start", Reference("instant")));
            var scheduleSummary = Summary(Element("planningHorizon", Reference("Period")));
            _model.StructureDefinitionSummaryProvider.Provide("Slot").Returns(summary);
            _model.StructureDefinitionSummaryProvider.Provide("Schedule").Returns(scheduleSummary);
            _rewriter = new InstantDateTimeRewriter(_model);
        }

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

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Slot")]
        [InlineData("slot.start")]
        [InlineData("SLOT.start")]
        [InlineData("Slot.Start")]
        [InlineData("Slot.unknown")]
        [InlineData("Unknown.start")]
        [InlineData("Slot.start.value")]
        [InlineData(" Slot.start")]
        [InlineData("Slot.start ")]
        [InlineData("Slot..start")]
        [InlineData("Slot.start | Slot.end")]
        [InlineData("(Slot.start)")]
        [InlineData("Slot.start.first()")]
        [InlineData("Slot.start as instant")]
        [InlineData("Slot.start.ofType(instant)")]
        [InlineData("Slot.start.where($this.exists())")]
        [InlineData("Slot.start[0]")]
        public void GivenUnsupportedExpressionOnSystemParameter_WhenRewritten_ThenExpressionIsUnchanged(string path)
        {
            // These deliberately altered definitions exercise the proof boundary, not bundled search parameters.
            var original = Expression.SearchParameter(Parameter(path), EndPredicate());

            Assert.Same(original, original.AcceptVisitor(_rewriter));
        }

        [Theory]
        [InlineData("Patient", "birthDate", "date")]
        [InlineData("Composition", "date", "dateTime")]
        [InlineData("Schedule", "planningHorizon", "Period")]
        public void GivenNonInstantFhirTemporalType_WhenRewritten_ThenExpressionIsUnchanged(
            string resourceType,
            string elementName,
            string fhirType)
        {
            // These real FHIR elements isolate leaf-type rejection. Bundled definitions are covered by SqlInstantDateTimeSearchTests.
            _model.IsKnownResource(resourceType, false).Returns(true);
            var summary = Summary(Element(elementName, Reference(fhirType)));
            _model.StructureDefinitionSummaryProvider.Provide(resourceType).Returns(summary);
            var parameter = new SearchParameterInfo(
                "date",
                "date",
                SearchParamType.Date,
                expression: $"{resourceType}.{elementName}")
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
                    expression: "Slot.start"),
                Parameter(type: SearchParamType.Composite),
                Parameter(type: SearchParamType.String),
                new SearchParameterInfo(
                    "start",
                    "start",
                    SearchParamType.Date,
                    components: new[] { new SearchParameterComponentInfo(new Uri("http://example.org/component"), "start") },
                    expression: "Slot.start") { IsSystemDefined = true },
            };
            foreach (SearchParameterInfo parameter in parameters)
            {
                var original = Expression.SearchParameter(parameter, EndPredicate());
                Assert.Same(original, original.AcceptVisitor(_rewriter));
            }
        }

        [Theory]
        [InlineData("choice")]
        [InlineData("mixed")]
        [InlineData("unresolved")]
        [InlineData("unknown-type")]
        [InlineData("empty-type")]
        [InlineData("null-type")]
        [InlineData("ambiguous")]
        public void GivenInvalidSlotStructureSummary_WhenRewritten_ThenExpressionIsUnchanged(string scenario)
        {
            // Slot.start is an instant. Malformed provider metadata must not authorize a rewrite.
            var element = Element("start", Reference("instant"));
            if (scenario == "choice")
            {
                element.IsChoiceElement.Returns(true);
            }
            else if (scenario == "mixed")
            {
                var types = new[] { Reference("instant"), Reference("dateTime") };
                element.Type.Returns(types);
            }
            else if (scenario == "unknown-type")
            {
                var types = new[] { Substitute.For<ITypeSerializationInfo>() };
                element.Type.Returns(types);
            }
            else if (scenario == "empty-type")
            {
                element.Type.Returns(Array.Empty<ITypeSerializationInfo>());
            }
            else if (scenario == "null-type")
            {
                ITypeSerializationInfo[] types = null;
                element.Type.Returns(types);
            }

            IStructureDefinitionSummary summary = scenario switch
            {
                "unresolved" => null,
                "ambiguous" => Summary(element, element),
                _ => Summary(element),
            };
            _model.StructureDefinitionSummaryProvider.Provide("Slot").Returns(summary);
            var original = Expression.SearchParameter(Parameter(), EndPredicate());

            Assert.Same(original, original.AcceptVisitor(_rewriter));
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
            };

        private static BinaryExpression EndPredicate() => Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, Boundary);

        private static IStructureDefinitionReference Reference(string type)
        {
            var reference = Substitute.For<IStructureDefinitionReference>();
            reference.ReferredType.Returns(type);
            return reference;
        }

        private static IElementDefinitionSummary Element(string name, params ITypeSerializationInfo[] types)
        {
            var element = Substitute.For<IElementDefinitionSummary>();
            element.ElementName.Returns(name);
            element.Type.Returns(types);
            return element;
        }

        private static IStructureDefinitionSummary Summary(params IElementDefinitionSummary[] elements)
        {
            var summary = Substitute.For<IStructureDefinitionSummary>();
            summary.GetElements().Returns(elements);
            return summary;
        }
    }
}

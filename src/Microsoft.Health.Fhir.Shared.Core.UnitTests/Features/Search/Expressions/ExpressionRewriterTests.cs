// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Expressions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ExpressionRewriterTests
    {
        [Fact]
        public void GivenANoopRewriter_WhenVisiting_ReturnsTheSameExpressionInstances()
        {
            var expressionRewriter = new NoopRewriter();

            void VerifyVisit(Expression expression)
            {
                Assert.Same(expression, expression.AcceptVisitor(expressionRewriter, null));
            }

            var simpleExpression1 = Expression.Equals(FieldName.Number, null, 1M);
            var simpleExpression2 = Expression.Equals(FieldName.Number, null, 5M);
            VerifyVisit(simpleExpression1);
            VerifyVisit(Expression.SearchParameter(new SearchParameterInfo("my-param", "my-param"), simpleExpression1));
            VerifyVisit(Expression.Chained(new[] { "Observation" }, new SearchParameterInfo("subject", "subject"), new[] { "Patient" }, false, simpleExpression1));
            VerifyVisit(Expression.Chained(new[] { "Patient" }, new SearchParameterInfo("subject", "subject"), new[] { "Observation" }, true, simpleExpression1));
            VerifyVisit(Expression.CompartmentSearch("Patient", "x"));
            VerifyVisit(Expression.Missing(FieldName.Quantity, null));
            VerifyVisit(Expression.MissingSearchParameter(new SearchParameterInfo("my-param", "my-param"), true));
            VerifyVisit(Expression.Or(simpleExpression1, simpleExpression2));
            VerifyVisit(Expression.StringEquals(FieldName.String, null, "Bob", true));
        }

        [Fact]
        public void GivenARewriterThatTurnsValuesIntoRanges_WhenVisiting_ReplacesSubexpressions()
        {
            var expressionRewriter = new RangeRewriter();

            void VerifyVisit(string expected, Expression inputExpression)
            {
                Assert.Equal(expected, inputExpression.AcceptVisitor(expressionRewriter, null).ToString());
            }

            var simpleExpression1 = Expression.Equals(FieldName.Number, null, 1M);
            var simpleExpression2 = Expression.Equals(FieldName.Number, null, 5M);
            string expectedAndString1 = "(And (FieldGreaterThanOrEqual Number 0) (FieldLessThanOrEqual Number 2))";
            string expectedAndString2 = "(And (FieldGreaterThanOrEqual Number 4) (FieldLessThanOrEqual Number 6))";

            VerifyVisit(expectedAndString1, simpleExpression1);

            VerifyVisit($"(Param my-param {expectedAndString1})", Expression.SearchParameter(new SearchParameterInfo("my-param", "my-param"), simpleExpression1));

            VerifyVisit($"(Chain subject:Patient {expectedAndString1})", Expression.Chained(new[] { "Observation" }, new SearchParameterInfo("subject", "subject"), new[] { "Patient" }, false, simpleExpression1));
            VerifyVisit($"(Reverse Chain subject:Observation {expectedAndString1})", Expression.Chained(new[] { "Patient" }, new SearchParameterInfo("subject", "subject"), new[] { "Observation" }, true, simpleExpression1));
            VerifyVisit($"(Or {expectedAndString1} {expectedAndString2})", Expression.Or(simpleExpression1, simpleExpression2));
        }

        [Theory]
        [InlineData(MultiaryOperator.And, false)]
        [InlineData(MultiaryOperator.And, true)]
        [InlineData(MultiaryOperator.Or, false)]
        [InlineData(MultiaryOperator.Or, true)]
        public void GivenAMultiaryExpression_WhenChildIsRewritten_ThenSmartScopeMarkerIsPreserved(MultiaryOperator operation, bool smartScope)
        {
            // Arrange
            var input = new MultiaryExpression(operation, new[] { Expression.Equals(FieldName.Number, null, 1M) })
            {
                IsSmartV2UnionExpressionForScopesSearchParameters = smartScope,
            };

            // Act
            var output = Assert.IsType<MultiaryExpression>(input.AcceptVisitor(new RangeRewriter(), null));

            // Assert
            Assert.NotSame(input, output);
            Assert.Equal(operation, output.MultiaryOperation);
            Assert.Equal(smartScope, output.IsSmartV2UnionExpressionForScopesSearchParameters);
            Assert.Equal("(And (FieldGreaterThanOrEqual Number 0) (FieldLessThanOrEqual Number 2))", Assert.Single(output.Expressions).ToString());
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void GivenAUnionWithNestedAndExpressions_WhenChildIsRewritten_ThenEachSmartScopeMarkerIsPreserved(bool unionMarker, bool andMarker)
        {
            // Arrange
            var innerAnd = Expression.And(Expression.Equals(FieldName.Number, null, 1M));
            innerAnd.IsSmartV2UnionExpressionForScopesSearchParameters = andMarker;
            var outerAnd = Expression.And(innerAnd);
            outerAnd.IsSmartV2UnionExpressionForScopesSearchParameters = andMarker;
            var input = Expression.Union(UnionOperator.All, new[] { outerAnd });
            input.IsSmartV2UnionExpressionForScopesSearchParameters = unionMarker;

            // Act
            var output = Assert.IsType<UnionExpression>(input.AcceptVisitor(new RangeRewriter(), null));

            // Assert
            Assert.NotSame(input, output);
            Assert.Equal(UnionOperator.All, output.Operator);
            Assert.Equal(unionMarker, output.IsSmartV2UnionExpressionForScopesSearchParameters);
            var rewrittenOuterAnd = Assert.IsType<MultiaryExpression>(Assert.Single(output.Expressions));
            var rewrittenInnerAnd = Assert.IsType<MultiaryExpression>(Assert.Single(rewrittenOuterAnd.Expressions));
            Assert.NotSame(outerAnd, rewrittenOuterAnd);
            Assert.NotSame(innerAnd, rewrittenInnerAnd);
            Assert.Equal(andMarker, rewrittenOuterAnd.IsSmartV2UnionExpressionForScopesSearchParameters);
            Assert.Equal(andMarker, rewrittenInnerAnd.IsSmartV2UnionExpressionForScopesSearchParameters);
            Assert.False(Assert.Single(rewrittenInnerAnd.Expressions).IsSmartV2UnionExpressionForScopesSearchParameters);
        }

        [Fact]
        public void GivenAMarkedUnionWithUnchangedChildren_WhenVisited_ThenOriginalInstancesAreReturned()
        {
            // Arrange
            var andExpression = Expression.And(Expression.Equals(FieldName.Number, null, 1M));
            andExpression.IsSmartV2UnionExpressionForScopesSearchParameters = true;
            var input = Expression.Union(UnionOperator.All, new[] { andExpression });
            input.IsSmartV2UnionExpressionForScopesSearchParameters = true;

            // Act
            Expression output = input.AcceptVisitor(new NoopRewriter(), null);

            // Assert
            Assert.Same(input, output);
            Assert.Same(andExpression, Assert.Single(input.Expressions));
            Assert.True(output.IsSmartV2UnionExpressionForScopesSearchParameters);
            Assert.True(andExpression.IsSmartV2UnionExpressionForScopesSearchParameters);
        }

        public class NoopRewriter : ExpressionRewriter<object>
        {
        }

        public class RangeRewriter : ExpressionRewriter<object>
        {
            public override Expression VisitBinary(BinaryExpression expression, object context)
            {
                // turn "Field = x" into "Field >= (x-1) and Field < (x+1)
                if (expression.BinaryOperator == BinaryOperator.Equal && expression.Value is decimal decimalValue)
                {
                    return Expression.And(
                        Expression.GreaterThanOrEqual(fieldName: expression.FieldName, expression.ComponentIndex, decimalValue - 1),
                        Expression.LessThanOrEqual(fieldName: expression.FieldName, expression.ComponentIndex, decimalValue + 1));
                }

                return expression;
            }
        }
    }
}

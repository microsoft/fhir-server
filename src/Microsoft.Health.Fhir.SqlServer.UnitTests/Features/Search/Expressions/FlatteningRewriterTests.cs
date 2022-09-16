// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    [Trait(Traits.Category, Categories.Search)]
    public class FlatteningRewriterTests
    {
        [Fact]
        public void GivenAMultiaryExpressionWithASingleElement_WhenFlattened_RemovesTheMultiary()
        {
            MultiaryExpression inputExpression = Expression.And(Expression.Equals(FieldName.Number, null, 1));
            Expression visitedExpression = inputExpression.AcceptVisitor(FlatteningRewriter.Instance);
            Assert.Equal("(FieldEqual Number 1)", visitedExpression.ToString());
        }

        [Fact]
        public void GivenTwoLayersOfAndExpressions_WhenFlattened_CombinesToOneAndExpression()
        {
            MultiaryExpression inputExpression =
                Expression.And(
                    Expression.And(Expression.GreaterThan(FieldName.Number, null, 1), Expression.LessThan(FieldName.Number, null, 5)),
                    Expression.And(Expression.GreaterThan(FieldName.Quantity, null, 1), Expression.LessThan(FieldName.Quantity, null, 5)));

            Expression visitedExpression = inputExpression.AcceptVisitor(FlatteningRewriter.Instance);
            Assert.Equal("(And (FieldGreaterThan Number 1) (FieldLessThan Number 5) (FieldGreaterThan Quantity 1) (FieldLessThan Quantity 5))", visitedExpression.ToString());
        }

        [Fact]
        public void GivenTwoLayersOfOrExpressions_WhenFlattened_CombinesToOneOrExpression()
        {
            MultiaryExpression inputExpression =
                Expression.Or(
                    Expression.Or(Expression.GreaterThan(FieldName.Number, null, 1), Expression.LessThan(FieldName.Number, null, 5)),
                    Expression.Or(Expression.GreaterThan(FieldName.Quantity, null, 1), Expression.LessThan(FieldName.Quantity, null, 5)));

            Expression visitedExpression = inputExpression.AcceptVisitor(FlatteningRewriter.Instance);
            Assert.Equal("(Or (FieldGreaterThan Number 1) (FieldLessThan Number 5) (FieldGreaterThan Quantity 1) (FieldLessThan Quantity 5))", visitedExpression.ToString());
        }

        [Fact]
        public void GivenAnOrExpressionWithAnAndChild_WhenFlattened_RemainsTheSame()
        {
            MultiaryExpression inputExpression =
                Expression.Or(
                    Expression.And(Expression.GreaterThan(FieldName.Number, null, 1), Expression.LessThan(FieldName.Number, null, 5)),
                    Expression.And(Expression.GreaterThan(FieldName.Quantity, null, 1), Expression.LessThan(FieldName.Quantity, null, 5)));

            Expression visitedExpression = inputExpression.AcceptVisitor(FlatteningRewriter.Instance);
            Assert.Equal("(Or (And (FieldGreaterThan Number 1) (FieldLessThan Number 5)) (And (FieldGreaterThan Quantity 1) (FieldLessThan Quantity 5)))", visitedExpression.ToString());
        }
    }
}

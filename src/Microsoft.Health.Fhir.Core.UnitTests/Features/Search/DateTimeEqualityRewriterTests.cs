// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class DateTimeEqualityRewriterTests
    {
        private static readonly DateTime _start = new(2021, 1, 1, 0, 0, 0);
        private static readonly DateTime _end = new(2021, 1, 1, 23, 59, 59);

        [Fact]
        public void GivenStartAndEndExpressions_WhenRewritten_AnUpperBoundOnStartIsAdded()
        {
            MultiaryExpression inputExpression =
                Expression.And(
                    Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, _start),
                    Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, _end));

            Expression rewrittenExpression = inputExpression.AcceptVisitor(DateTimeEqualityRewriter.Instance);

            Assert.Equal(
                "(And (FieldGreaterThanOrEqual DateTimeStart 2021-01-01T00:00:00.0000000) (FieldLessThanOrEqual DateTimeStart 2021-01-01T23:59:59.0000000) (FieldLessThanOrEqual DateTimeEnd 2021-01-01T23:59:59.0000000))",
                rewrittenExpression.ToString());
        }

        [Fact]
        public void GivenStartAndEndExpressionsOredTogether_WhenRewritten_AreIgnored()
        {
            MultiaryExpression inputExpression =
                Expression.Or(
                    Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, _start),
                    Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, _end));

            Expression rewrittenExpression = inputExpression.AcceptVisitor(DateTimeEqualityRewriter.Instance);

            Assert.Same(inputExpression, rewrittenExpression);
        }

        [Fact]
        public void GivenStartAndEndExpressionsInReverseOrder_WhenRewritten_AnUpperBoundOnStartIsAdded()
        {
            MultiaryExpression inputExpression =
                Expression.And(
                    Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, _end),
                    Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, _start));

            Expression rewrittenExpression = inputExpression.AcceptVisitor(DateTimeEqualityRewriter.Instance);

            Assert.Equal(
                "(And (FieldGreaterThanOrEqual DateTimeStart 2021-01-01T00:00:00.0000000) (FieldLessThanOrEqual DateTimeStart 2021-01-01T23:59:59.0000000) (FieldLessThanOrEqual DateTimeEnd 2021-01-01T23:59:59.0000000))",
                rewrittenExpression.ToString());
        }

        [Fact]
        public void GivenStartAndEndExclusiveExpressions_WhenRewritten_AnUpperBoundOnStartIsAdded()
        {
            MultiaryExpression inputExpression =
                Expression.And(
                    Expression.GreaterThan(FieldName.DateTimeStart, null, _start),
                    Expression.LessThan(FieldName.DateTimeEnd, null, _end));

            Expression rewrittenExpression = inputExpression.AcceptVisitor(DateTimeEqualityRewriter.Instance);

            Assert.Equal(
                "(And (FieldGreaterThan DateTimeStart 2021-01-01T00:00:00.0000000) (FieldLessThan DateTimeStart 2021-01-01T23:59:59.0000000) (FieldLessThan DateTimeEnd 2021-01-01T23:59:59.0000000))",
                rewrittenExpression.ToString());
        }

        [Fact]
        public void GivenTwoStartExpressions_WhenRewritten_AreIgnored()
        {
            MultiaryExpression inputExpression =
                Expression.And(
                    Expression.GreaterThan(FieldName.DateTimeStart, null, _start),
                    Expression.LessThan(FieldName.DateTimeStart, null, _end));

            Expression rewrittenExpression = inputExpression.AcceptVisitor(DateTimeEqualityRewriter.Instance);

            Assert.Same(inputExpression, rewrittenExpression);
        }

        [Fact]
        public void GiveThreeUnrelatedExpressions_WhenRewritten_AreIgnored()
        {
            MultiaryExpression inputExpression =
                Expression.And(
                    Expression.Equals(FieldName.Number, null, 1),
                    Expression.Equals(FieldName.Number, null, 2),
                    Expression.Equals(FieldName.Number, null, 3));

            Expression rewrittenExpression = inputExpression.AcceptVisitor(DateTimeEqualityRewriter.Instance);

            Assert.Same(inputExpression, rewrittenExpression);
        }

        [Fact]
        public void GivenStartAndEndExpressionsAndAnUnrelatedExpression_WhenRewritten_AnUpperBoundOnStartIsAdded()
        {
            MultiaryExpression inputExpression =
                Expression.And(
                    Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, _start),
                    Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, _end),
                    Expression.Equals(FieldName.Number, null, 1));

            Expression rewrittenExpression = inputExpression.AcceptVisitor(DateTimeEqualityRewriter.Instance);

            Assert.Equal(
                "(And (FieldGreaterThanOrEqual DateTimeStart 2021-01-01T00:00:00.0000000) (FieldLessThanOrEqual DateTimeStart 2021-01-01T23:59:59.0000000) (FieldLessThanOrEqual DateTimeEnd 2021-01-01T23:59:59.0000000) (FieldEqual Number 1))",
                rewrittenExpression.ToString());
        }

        [Fact]
        public void GivenStartAndEndExpressionsOnDifferentComponents_WhenRewritten_AreIgnored()
        {
            MultiaryExpression inputExpression =
                Expression.And(
                    Expression.GreaterThanOrEqual(FieldName.DateTimeStart, 1, _start),
                    Expression.LessThanOrEqual(FieldName.DateTimeEnd, 2, _end));

            Expression rewrittenExpression = inputExpression.AcceptVisitor(DateTimeEqualityRewriter.Instance);

            Assert.Same(inputExpression, rewrittenExpression);
        }
    }
}

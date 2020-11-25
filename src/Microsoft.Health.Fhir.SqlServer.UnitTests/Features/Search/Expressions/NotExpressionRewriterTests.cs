// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    public class NotExpressionRewriterTests
    {
        [Fact]
        public void GivenExpressionWithNotExpression_WhenVisited_AllExpressionPrependedToExpressionList()
        {
            var subExpression = Expression.StringEquals(FieldName.TokenCode, 0, "TestValue123", false);
            var tableExpressions = new List<TableExpression>
            {
                new TableExpression(null, new SearchParameterExpression(new SearchParameterInfo("TestParam"), Expression.Not(subExpression)), null, TableExpressionKind.Normal),
            };

            var inputExpression = SqlRootExpression.WithTableExpressions(tableExpressions);
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(NotExpressionRewriter.Instance);
            Assert.Collection(
                visitedExpression.TableExpressions,
                e => { Assert.Equal(TableExpressionKind.All, e.Kind); },
                e => { ValidateNotExpression(subExpression, e); });
            Assert.Equal(tableExpressions.Count + 1, visitedExpression.TableExpressions.Count);
        }

        [Fact]
        public void GivenExpressionWithNoNotExpression_WhenVisited_OriginalExpressionReturned()
        {
            var tableExpressions = new List<TableExpression>
            {
                new TableExpression(null, null, null, TableExpressionKind.Normal),
            };

            var inputExpression = SqlRootExpression.WithTableExpressions(tableExpressions);
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(NotExpressionRewriter.Instance);
            Assert.Equal(inputExpression, visitedExpression);
        }

        [Fact]
        public void GivenExpressionWithNotExpressionLast_WhenVisited_NotExpressionUnwrapped()
        {
            var subExpression = Expression.StringEquals(FieldName.TokenCode, 0, "TestValue123", false);
            var tableExpressions = new List<TableExpression>
            {
                new TableExpression(null, null, null, TableExpressionKind.Normal),
                new TableExpression(null, new SearchParameterExpression(new SearchParameterInfo("TestParam"), Expression.Not(subExpression)), null, TableExpressionKind.Normal),
            };

            var inputExpression = SqlRootExpression.WithTableExpressions(tableExpressions);
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(NotExpressionRewriter.Instance);
            Assert.Collection(
                visitedExpression.TableExpressions,
                e => { Assert.Equal(tableExpressions[0], e); },
                e => { ValidateNotExpression(subExpression, e); });
        }

        private static void ValidateNotExpression(Expression subExpression, TableExpression expressionToValidate)
        {
            Assert.Equal(TableExpressionKind.NotExists, expressionToValidate.Kind);

            var spExpression = Assert.IsType<SearchParameterExpression>(expressionToValidate.NormalizedPredicate);
            Assert.Equal(subExpression, spExpression.Expression);
        }
    }
}

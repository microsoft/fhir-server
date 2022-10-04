// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class NotExpressionRewriterTests
    {
        [Fact]
        public void GivenExpressionWithNotExpression_WhenVisited_AllExpressionPrependedToExpressionList()
        {
            var subExpression = Expression.StringEquals(FieldName.TokenCode, 0, "TestValue123", false);
            var searchParamTableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, new SearchParameterExpression(new SearchParameterInfo("TestParam", "TestParam"), Expression.Not(subExpression)), SearchParamTableExpressionKind.Normal),
            };

            var inputExpression = SqlRootExpression.WithSearchParamTableExpressions(searchParamTableExpressions);
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(NotExpressionRewriter.Instance);
            Assert.Collection(
                visitedExpression.SearchParamTableExpressions,
                e => { Assert.Equal(SearchParamTableExpressionKind.Normal, e.Kind); },
                e => { ValidateNotExpression(subExpression, e); });
            Assert.Equal(searchParamTableExpressions.Count + 1, visitedExpression.SearchParamTableExpressions.Count);
        }

        [Fact]
        public void GivenExpressionWithNoNotExpression_WhenVisited_OriginalExpressionReturned()
        {
            var searchParamTableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Normal),
            };

            var inputExpression = SqlRootExpression.WithSearchParamTableExpressions(searchParamTableExpressions);
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(NotExpressionRewriter.Instance);
            Assert.Equal(inputExpression, visitedExpression);
        }

        [Fact]
        public void GivenExpressionWithNotExpressionLast_WhenVisited_NotExpressionUnwrapped()
        {
            var subExpression = Expression.StringEquals(FieldName.TokenCode, 0, "TestValue123", false);
            var searchParamTableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Normal),
                new SearchParamTableExpression(null, new SearchParameterExpression(new SearchParameterInfo("TestParam", "TestParam"), Expression.Not(subExpression)), SearchParamTableExpressionKind.Normal),
            };

            var inputExpression = SqlRootExpression.WithSearchParamTableExpressions(searchParamTableExpressions);
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(NotExpressionRewriter.Instance);
            Assert.Collection(
                visitedExpression.SearchParamTableExpressions,
                e => { Assert.Equal(searchParamTableExpressions[0], e); },
                e => { ValidateNotExpression(subExpression, e); });
        }

        private static void ValidateNotExpression(Expression subExpression, SearchParamTableExpression expressionToValidate)
        {
            Assert.Equal(SearchParamTableExpressionKind.NotExists, expressionToValidate.Kind);

            var spExpression = Assert.IsType<SearchParameterExpression>(expressionToValidate.Predicate);
            Assert.Equal(subExpression, spExpression.Expression);
        }
    }
}

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
    public class MissingSearchParamVisitorTests
    {
        [Fact]
        public void GivenExpressionWithMissingParameterExpression_WhenVisited_AllExpressionPrependedToExpressionList()
        {
            var tableExpressions = new List<TableExpression>
            {
                new TableExpression(null, new MissingSearchParameterExpression(new SearchParameterInfo("TestParam"), true), null, TableExpressionKind.Normal),
            };

            var inputExpression = SqlRootExpression.WithTableExpressions(tableExpressions);
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(MissingSearchParamVisitor.Instance);
            Assert.Collection(
                visitedExpression.TableExpressions,
                e => { Assert.Equal(TableExpressionKind.All, e.Kind); },
                e => { Assert.NotNull(e.NormalizedPredicate as MissingSearchParameterExpression); });
            Assert.Equal(tableExpressions.Count + 1, visitedExpression.TableExpressions.Count);
        }

        [Fact]
        public void GivenExpressionWithNoMissingParameterExpression_WhenVisited_OriginalExpressionReturned()
        {
            var tableExpressions = new List<TableExpression>
            {
                new TableExpression(null, null, null, TableExpressionKind.Normal),
            };

            var inputExpression = SqlRootExpression.WithTableExpressions(tableExpressions);
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(MissingSearchParamVisitor.Instance);
            Assert.Equal(inputExpression, visitedExpression);
        }

        [Fact]
        public void GivenExpressionWithMissingParameterExpressionFalseLast_WhenVisited_OriginalExpressionReturned()
        {
            var tableExpressions = new List<TableExpression>
            {
                new TableExpression(null, null, null, TableExpressionKind.Normal),
                new TableExpression(null, new MissingSearchParameterExpression(new SearchParameterInfo("TestParam"), false), null, TableExpressionKind.Normal),
            };

            var inputExpression = SqlRootExpression.WithTableExpressions(tableExpressions);
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(MissingSearchParamVisitor.Instance);
            Assert.Equal(inputExpression, visitedExpression);
        }

        [Fact]
        public void GivenExpressionWithMissingParameterExpressionLast_WhenVisited_MissingParameterExpressionNegated()
        {
            var tableExpressions = new List<TableExpression>
            {
                new TableExpression(null, null, null, TableExpressionKind.Normal),
                new TableExpression(null, new MissingSearchParameterExpression(new SearchParameterInfo("TestParam"), true), null, TableExpressionKind.Normal),
            };

            var inputExpression = SqlRootExpression.WithTableExpressions(tableExpressions);
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(MissingSearchParamVisitor.Instance);
            Assert.Collection(
                visitedExpression.TableExpressions,
                e => { Assert.Equal(tableExpressions[0], e); },
                e => { Assert.Equal(TableExpressionKind.NotExists, e.Kind); });
        }
    }
}

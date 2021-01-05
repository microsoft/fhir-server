// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    public class TableExpressionReordererTests
    {
        private static readonly Expression NormalExpression = new SearchParameterExpression(new SearchParameterInfo("TestParam"), Expression.Equals(FieldName.TokenCode, null, "TestValue"));

        [Fact]
        public void GivenExpressionWithSingleTableExpression_WhenReordered_ReturnsOriginalExpression()
        {
            var inputExpression = SqlRootExpression.WithTableExpressions(
                new TableExpression(null, null, TableExpressionKind.Normal));
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(TableExpressionReorderer.Instance);
            Assert.Equal(inputExpression, visitedExpression);
        }

        [Fact]
        public void GivenExpressionWithMulthMultipleTableExpressions_WhenReordered_DenormilizedExpressionReturnedFirst()
        {
            var tableExpressions = new List<TableExpression>
            {
                new TableExpression(new ReferenceQueryGenerator(), NormalExpression, TableExpressionKind.Normal),
                new TableExpression(null, null, TableExpressionKind.All),
            };

            var inputExpression = SqlRootExpression.WithTableExpressions(tableExpressions);
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(TableExpressionReorderer.Instance);
            Assert.Collection(visitedExpression.TableExpressions, new[] { 1, 0 }.Select<int, Action<TableExpression>>(x => e => Assert.Equal(tableExpressions[x], e)).ToArray());
        }

        [Fact]
        public void GivenExpressionWithMulthMultipleTableExpressions_WhenReordered_ReferenceExpressionReturnedBeforeNormal()
        {
            var tableExpressions = new List<TableExpression>
            {
                new TableExpression(null, NormalExpression, TableExpressionKind.Normal),
                new TableExpression(new ReferenceQueryGenerator(), NormalExpression, TableExpressionKind.Normal),
            };

            var inputExpression = SqlRootExpression.WithTableExpressions(tableExpressions);
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(TableExpressionReorderer.Instance);
            Assert.Collection(visitedExpression.TableExpressions, new[] { 1, 0 }.Select<int, Action<TableExpression>>(x => e => Assert.Equal(tableExpressions[x], e)).ToArray());
        }

        [Fact]
        public void GivenExpressionWithMulthMultipleTableExpressions_WhenReordered_CompartmentExpressionReturnedBeforeNormal()
        {
            var tableExpressions = new List<TableExpression>
            {
                new TableExpression(null, NormalExpression, TableExpressionKind.Normal),
                new TableExpression(new CompartmentQueryGenerator(), NormalExpression, TableExpressionKind.Normal),
            };

            var inputExpression = SqlRootExpression.WithTableExpressions(tableExpressions);
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(TableExpressionReorderer.Instance);
            Assert.Collection(visitedExpression.TableExpressions, new[] { 1, 0 }.Select<int, Action<TableExpression>>(x => e => Assert.Equal(tableExpressions[x], e)).ToArray());
        }

        [Fact]
        public void GivenExpressionWithMulthMultipleTableExpressions_WhenReordered_MissingParameterExpressionReturnedBeforeInclude()
        {
            var tableExpressions = new List<TableExpression>
            {
                new TableExpression(new IncludeQueryGenerator(), NormalExpression, TableExpressionKind.Include),
                new TableExpression(null, new MissingSearchParameterExpression(new SearchParameterInfo("TestParam"), true), TableExpressionKind.Normal),
            };

            var inputExpression = SqlRootExpression.WithTableExpressions(tableExpressions);
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(TableExpressionReorderer.Instance);
            Assert.Collection(visitedExpression.TableExpressions, new[] { 1, 0 }.Select<int, Action<TableExpression>>(x => e => Assert.Equal(tableExpressions[x], e)).ToArray());
        }

        [Fact]
        public void GivenExpressionWithMulthMultipleTableExpressions_WhenReordered_IncludeExpressionReturnedLast()
        {
            var tableExpressions = new List<TableExpression>
            {
                new TableExpression(new IncludeQueryGenerator(), NormalExpression, TableExpressionKind.Include),
                new TableExpression(null, NormalExpression, TableExpressionKind.Normal),
            };

            var inputExpression = SqlRootExpression.WithTableExpressions(tableExpressions);
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(TableExpressionReorderer.Instance);
            Assert.Collection(visitedExpression.TableExpressions, new[] { 1, 0 }.Select<int, Action<TableExpression>>(x => e => Assert.Equal(tableExpressions[x], e)).ToArray());
        }
    }
}

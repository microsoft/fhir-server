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
    public class SearchParamTableExpressionReordererTests
    {
        private static readonly Expression NormalExpression = new SearchParameterExpression(new SearchParameterInfo("TestParam"), Expression.Equals(FieldName.TokenCode, null, "TestValue"));

        [Fact]
        public void GivenExpressionWithSingleTableExpression_WhenReordered_ReturnsOriginalExpression()
        {
            var inputExpression = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Normal));
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(SearchParamTableExpressionReorderer.Instance);
            Assert.Equal(inputExpression, visitedExpression);
        }

        [Fact]
        public void GivenExpressionWithMulthMultipleTableExpressions_WhenReordered_DenormilizedExpressionReturnedFirst()
        {
            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(new ReferenceQueryGenerator(), NormalExpression, SearchParamTableExpressionKind.Normal),
                new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.All),
            };

            var inputExpression = SqlRootExpression.WithSearchParamTableExpressions(tableExpressions);
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(SearchParamTableExpressionReorderer.Instance);
            Assert.Collection(visitedExpression.SearchParamTableExpressions, new[] { 1, 0 }.Select<int, Action<SearchParamTableExpression>>(x => e => Assert.Equal(tableExpressions[x], e)).ToArray());
        }

        [Fact]
        public void GivenExpressionWithMulthMultipleTableExpressions_WhenReordered_ReferenceExpressionReturnedBeforeNormal()
        {
            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, NormalExpression, SearchParamTableExpressionKind.Normal),
                new SearchParamTableExpression(new ReferenceQueryGenerator(), NormalExpression, SearchParamTableExpressionKind.Normal),
            };

            var inputExpression = SqlRootExpression.WithSearchParamTableExpressions(tableExpressions);
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(SearchParamTableExpressionReorderer.Instance);
            Assert.Collection(visitedExpression.SearchParamTableExpressions, new[] { 1, 0 }.Select<int, Action<SearchParamTableExpression>>(x => e => Assert.Equal(tableExpressions[x], e)).ToArray());
        }

        [Fact]
        public void GivenExpressionWithMulthMultipleTableExpressions_WhenReordered_CompartmentExpressionReturnedBeforeNormal()
        {
            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, NormalExpression, SearchParamTableExpressionKind.Normal),
                new SearchParamTableExpression(new CompartmentQueryGenerator(), NormalExpression, SearchParamTableExpressionKind.Normal),
            };

            var inputExpression = SqlRootExpression.WithSearchParamTableExpressions(tableExpressions);
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(SearchParamTableExpressionReorderer.Instance);
            Assert.Collection(visitedExpression.SearchParamTableExpressions, new[] { 1, 0 }.Select<int, Action<SearchParamTableExpression>>(x => e => Assert.Equal(tableExpressions[x], e)).ToArray());
        }

        [Fact]
        public void GivenExpressionWithMulthMultipleTableExpressions_WhenReordered_MissingParameterExpressionReturnedBeforeInclude()
        {
            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(new IncludeQueryGenerator(), NormalExpression, SearchParamTableExpressionKind.Include),
                new SearchParamTableExpression(null, new MissingSearchParameterExpression(new SearchParameterInfo("TestParam"), true), SearchParamTableExpressionKind.Normal),
            };

            var inputExpression = SqlRootExpression.WithSearchParamTableExpressions(tableExpressions);
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(SearchParamTableExpressionReorderer.Instance);
            Assert.Collection(visitedExpression.SearchParamTableExpressions, new[] { 1, 0 }.Select<int, Action<SearchParamTableExpression>>(x => e => Assert.Equal(tableExpressions[x], e)).ToArray());
        }

        [Fact]
        public void GivenExpressionWithMulthMultipleTableExpressions_WhenReordered_IncludeExpressionReturnedLast()
        {
            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(new IncludeQueryGenerator(), NormalExpression, SearchParamTableExpressionKind.Include),
                new SearchParamTableExpression(null, NormalExpression, SearchParamTableExpressionKind.Normal),
            };

            var inputExpression = SqlRootExpression.WithSearchParamTableExpressions(tableExpressions);
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(SearchParamTableExpressionReorderer.Instance);
            Assert.Collection(visitedExpression.SearchParamTableExpressions, new[] { 1, 0 }.Select<int, Action<SearchParamTableExpression>>(x => e => Assert.Equal(tableExpressions[x], e)).ToArray());
        }
    }
}

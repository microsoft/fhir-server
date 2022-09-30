// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ResourceColumnPredicatePushdownRewriterTests
    {
        [Fact]
        public void GivenExpressionWithNoTableExpressions_WhenRewritten_ReturnsOriginalExpression()
        {
            var inputExpression = SqlRootExpression.WithResourceTableExpressions(
                Expression.SearchParameter(new SearchParameterInfo("abc", "abc"), Expression.Equals(FieldName.Number, null, 1)));
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(ResourceColumnPredicatePushdownRewriter.Instance);
            Assert.Equal(inputExpression, visitedExpression);
        }

        [Fact]
        public void GivenExpressionWithNoResourceColumnExpressions_WhenRewritten_ReturnsOriginalExpression()
        {
            var inputExpression = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Normal));
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(ResourceColumnPredicatePushdownRewriter.Instance);
            Assert.Equal(inputExpression, visitedExpression);
        }

        [Theory]
        [InlineData(SearchParameterNames.ResourceType)]
        [InlineData(SqlSearchParameters.ResourceSurrogateIdParameterName)]
        public void GivenExpressionWithExtractableResourceColumnExpression_WhenRewritten_CommonResourceExpressionAddedToTableExpressions(string paramName)
        {
            var inputExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(null, new SearchParameterExpression(new SearchParameterInfo("myParam", "myParam"), Expression.Equals(FieldName.String, null, "foo")), SearchParamTableExpressionKind.Normal),
                },
                new List<SearchParameterExpressionBase>
                {
                    new SearchParameterExpression(new SearchParameterInfo(paramName, paramName), Expression.Equals(FieldName.String, null, "TestParamValue")),
                });
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(ResourceColumnPredicatePushdownRewriter.Instance);

            Assert.Equal(
                Expression.And(inputExpression.SearchParamTableExpressions[0].Predicate, inputExpression.ResourceTableExpressions[0]).ToString(),
                visitedExpression.SearchParamTableExpressions[0].Predicate.ToString());
        }

        [Fact]
        public void GivenExpressionWithMultipleExtractableResourceColumnExpressions_WhenRewritten_CommonResourceExpressionsAddedToTableExpressions()
        {
            var inputExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Normal),
                },
                new List<SearchParameterExpressionBase>
                {
                    new SearchParameterExpression(new SearchParameterInfo(SearchParameterNames.ResourceType, SearchParameterNames.ResourceType), Expression.Equals(FieldName.String, null, "TestParamValue1")),
                    new SearchParameterExpression(new SearchParameterInfo(SqlSearchParameters.ResourceSurrogateIdParameterName, SqlSearchParameters.ResourceSurrogateIdParameterName), Expression.Equals(FieldName.String, null, "TestParamValue2")),
                });
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(ResourceColumnPredicatePushdownRewriter.Instance);
            Assert.Equal(Expression.And(inputExpression.ResourceTableExpressions).ToString(), visitedExpression.SearchParamTableExpressions[0].Predicate.ToString());
        }

        [Theory]
        [InlineData(SearchParameterNames.ResourceType)]
        [InlineData(SqlSearchParameters.ResourceSurrogateIdParameterName)]
        public void GivenExpressionWithMultipleResourceColumnExpressions_WhenRewritten_ResourceColumnPredicatesClearedAndReplacedWithAllExpression(string paramName)
        {
            var inputExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(null, new SearchParameterExpression(new SearchParameterInfo("myParam", "myParam"), Expression.Equals(FieldName.String, null, "foo")), SearchParamTableExpressionKind.Normal),
                },
                new List<SearchParameterExpressionBase>
                {
                    new SearchParameterExpression(new SearchParameterInfo(paramName, paramName), Expression.Equals(FieldName.String, null, "ExtractableTestParamValue")),
                    new SearchParameterExpression(new SearchParameterInfo(SearchParameterNames.Id, SearchParameterNames.Id), Expression.Equals(FieldName.String, null, "myid")),
                });
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(ResourceColumnPredicatePushdownRewriter.Instance);
            Assert.Empty(visitedExpression.ResourceTableExpressions);
            Assert.Equal(new SearchParamTableExpression(null, Expression.And(inputExpression.ResourceTableExpressions[0], inputExpression.ResourceTableExpressions[1]), SearchParamTableExpressionKind.All).ToString(), visitedExpression.SearchParamTableExpressions[0].ToString());
        }

        [Fact]
        public void GivenSqlRootExpressionWithResourceColumnPredicateAndOnlyIncludeTableExpression_WhenRewritten_ResourceColumnIsPreserved()
        {
            var inputExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Include),
                },
                new List<SearchParameterExpressionBase>
                {
                    new SearchParameterExpression(new SearchParameterInfo(SearchParameterNames.ResourceType, SearchParameterNames.ResourceType), Expression.Equals(FieldName.String, null, "TestParamValue1")),
                });
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(ResourceColumnPredicatePushdownRewriter.Instance);
            Assert.Same(inputExpression.ResourceTableExpressions, visitedExpression.ResourceTableExpressions);
        }

        [Theory]
        [InlineData(SearchParameterNames.ResourceType)]
        [InlineData(SqlSearchParameters.ResourceSurrogateIdParameterName)]
        public void GivenExpressionWithResourceColumnnAndChainedExpressions_WhenRewritten_ResourceColumnPredicatesPromotedToChainTableExpression(string paramName)
        {
            var inputExpression = new SqlRootExpression(
                new List<SearchParamTableExpression>
                {
                    new SearchParamTableExpression(
                        ChainLinkQueryGenerator.Instance,
                        new SqlChainLinkExpression(new[] { "Observation" }, new SearchParameterInfo("myref", "myref"), new[] { "Patient" }, false),
                        SearchParamTableExpressionKind.Chain,
                        1),
                    new SearchParamTableExpression(
                        null,
                        new SearchParameterExpression(new SearchParameterInfo("myParam", "myParam"), Expression.Equals(FieldName.String, null, "foo")),
                        SearchParamTableExpressionKind.Normal,
                        1),
                },
                new List<SearchParameterExpressionBase>
                {
                    new SearchParameterExpression(new SearchParameterInfo(paramName, paramName), Expression.Equals(FieldName.String, null, "ExtractableTestParamValue")),
                });
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(ResourceColumnPredicatePushdownRewriter.Instance);
            Assert.Equal(inputExpression.ResourceTableExpressions[0], ((SqlChainLinkExpression)visitedExpression.SearchParamTableExpressions[0].Predicate).ExpressionOnSource);
            Assert.Equal(inputExpression.SearchParamTableExpressions[0].ChainLevel, visitedExpression.SearchParamTableExpressions[0].ChainLevel);
            Assert.Same(inputExpression.SearchParamTableExpressions[1], visitedExpression.SearchParamTableExpressions[1]);
        }
    }
}

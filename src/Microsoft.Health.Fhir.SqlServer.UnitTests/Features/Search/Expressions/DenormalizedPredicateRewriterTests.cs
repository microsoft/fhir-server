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
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    public class DenormalizedPredicateRewriterTests
    {
        [Fact]
        public void GivenExpressionWithNoTableExpressions_WhenRewritten_ReturnsOriginalExpression()
        {
            var inputExpression = SqlRootExpression.WithDenormalizedExpressions(
                Expression.Equals(FieldName.Number, null, 1));
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(DenormalizedPredicateRewriter.Instance);
            Assert.Equal(inputExpression, visitedExpression);
        }

        [Fact]
        public void GivenExpressionWithNoDenormalizedExpressions_WhenRewritten_ReturnsOriginalExpression()
        {
            var inputExpression = SqlRootExpression.WithTableExpressions(
                new TableExpression(null, null, null, TableExpressionKind.Normal));
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(DenormalizedPredicateRewriter.Instance);
            Assert.Equal(inputExpression, visitedExpression);
        }

        [Theory]
        [InlineData(SearchParameterNames.ResourceType)]
        [InlineData(SqlSearchParameters.ResourceSurrogateIdParameterName)]
        public void GivenExpressionWithExtractableDenormalizedExpression_WhenRewritten_DenormalizedExpressionAddedToTableExpressions(string paramName)
        {
            var inputExpression = new SqlRootExpression(
                new List<TableExpression>
                {
                    new TableExpression(null, null, null, TableExpressionKind.Normal),
                },
                new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo(paramName), Expression.Equals(FieldName.String, null, "TestParamValue")),
                });
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(DenormalizedPredicateRewriter.Instance);
            Assert.Equal(inputExpression.DenormalizedExpressions[0], visitedExpression.TableExpressions[0].DenormalizedPredicate);
        }

        [Fact]
        public void GivenExpressionWithMultipleExtractableDenormalizedExpressions_WhenRewritten_DenormalizedExpressionsAddedToTableExpressions()
        {
            var inputExpression = new SqlRootExpression(
                new List<TableExpression>
                {
                    new TableExpression(null, null, null, TableExpressionKind.Normal),
                },
                new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo(SearchParameterNames.ResourceType), Expression.Equals(FieldName.String, null, "TestParamValue1")),
                    new SearchParameterExpression(new SearchParameterInfo(SqlSearchParameters.ResourceSurrogateIdParameterName), Expression.Equals(FieldName.String, null, "TestParamValue2")),
                });
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(DenormalizedPredicateRewriter.Instance);
            Assert.Equal(Expression.And(inputExpression.DenormalizedExpressions).ToString(), visitedExpression.TableExpressions[0].DenormalizedPredicate.ToString());
        }

        [Theory]
        [InlineData(SearchParameterNames.ResourceType)]
        [InlineData(SqlSearchParameters.ResourceSurrogateIdParameterName)]
        public void GivenExpressionWithMultipleDenormalizedExpressions_WhenRewritten_DenormalisedPredicatesClearedAndReplacedWithAllExpression(string paramName)
        {
            var inputExpression = new SqlRootExpression(
                new List<TableExpression>
                {
                    new TableExpression(null, null, null, TableExpressionKind.Normal),
                },
                new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo(paramName), Expression.Equals(FieldName.String, null, "ExtractableTestParamValue")),
                    new SearchParameterExpression(new SearchParameterInfo("TestParamName"), Expression.Equals(FieldName.String, null, "NonExtractableTestParamValue")),
                });
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(DenormalizedPredicateRewriter.Instance);
            Assert.Empty(visitedExpression.DenormalizedExpressions);
            Assert.Equal(new TableExpression(null, null, Expression.And(inputExpression.DenormalizedExpressions), TableExpressionKind.All).ToString(), visitedExpression.TableExpressions[0].ToString());
        }

        [Fact]
        public void GivenSqlRootExpressionWithDenormalizedPredicateAndOnlyIncludeTableExpression_WhenRewritten_DenormalizedPredicateIsPreserved()
        {
            var inputExpression = new SqlRootExpression(
                new List<TableExpression>
                {
                    new TableExpression(null, null, null, TableExpressionKind.Include),
                },
                new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo(SearchParameterNames.ResourceType), Expression.Equals(FieldName.String, null, "TestParamValue1")),
                });
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(DenormalizedPredicateRewriter.Instance);
            Assert.Same(inputExpression.DenormalizedExpressions, visitedExpression.DenormalizedExpressions);
        }

        [Theory]
        [InlineData(SearchParameterNames.ResourceType)]
        [InlineData(SqlSearchParameters.ResourceSurrogateIdParameterName)]
        public void GivenExpressionWithDenormalizedAndChainedExpressions_WhenRewritten_DenormalisedPredicatesPromotedToChainTableExpression(string paramName)
        {
            var inputExpression = new SqlRootExpression(
                new List<TableExpression>
                {
                    new TableExpression(null, null, null, TableExpressionKind.Chain, 1),
                },
                new List<Expression>
                {
                    new SearchParameterExpression(new SearchParameterInfo(paramName), Expression.Equals(FieldName.String, null, "ExtractableTestParamValue")),
                });
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(DenormalizedPredicateRewriter.Instance);
            Assert.Equal(inputExpression.DenormalizedExpressions[0], visitedExpression.TableExpressions[0].DenormalizedPredicateOnChainRoot);
            Assert.Equal(inputExpression.TableExpressions[0].ChainLevel, visitedExpression.TableExpressions[0].ChainLevel);
        }
    }
}

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
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    public class DenormalizedPredicateRewriterTests
    {
        [Fact]
        public void GivenExpressionWithNoTableExpressions_WhenRewritten_ReturnsOriginalExpression()
        {
            var inputExpression = SqlRootExpression.WithResourceExpressions(
                Expression.SearchParameter(new SearchParameterInfo("abc"), Expression.Equals(FieldName.Number, null, 1)));
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(DenormalizedPredicateRewriter.Instance);
            Assert.Equal(inputExpression, visitedExpression);
        }

        [Fact]
        public void GivenExpressionWithNoDenormalizedExpressions_WhenRewritten_ReturnsOriginalExpression()
        {
            var inputExpression = SqlRootExpression.WithTableExpressions(
                new TableExpression(null, null, TableExpressionKind.Normal));
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
                    new TableExpression(null, new SearchParameterExpression(new SearchParameterInfo("myParam"), Expression.Equals(FieldName.String, null, "foo")), TableExpressionKind.Normal),
                },
                new List<SearchParameterExpressionBase>
                {
                    new SearchParameterExpression(new SearchParameterInfo(paramName), Expression.Equals(FieldName.String, null, "TestParamValue")),
                });
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(DenormalizedPredicateRewriter.Instance);

            Assert.Equal(
                Expression.And(inputExpression.TableExpressions[0].NormalizedPredicate, inputExpression.ResourceExpressions[0]).ToString(),
                visitedExpression.TableExpressions[0].NormalizedPredicate.ToString());
        }

        [Fact]
        public void GivenExpressionWithMultipleExtractableDenormalizedExpressions_WhenRewritten_DenormalizedExpressionsAddedToTableExpressions()
        {
            var inputExpression = new SqlRootExpression(
                new List<TableExpression>
                {
                    new TableExpression(null, null, TableExpressionKind.Normal),
                },
                new List<SearchParameterExpressionBase>
                {
                    new SearchParameterExpression(new SearchParameterInfo(SearchParameterNames.ResourceType), Expression.Equals(FieldName.String, null, "TestParamValue1")),
                    new SearchParameterExpression(new SearchParameterInfo(SqlSearchParameters.ResourceSurrogateIdParameterName), Expression.Equals(FieldName.String, null, "TestParamValue2")),
                });
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(DenormalizedPredicateRewriter.Instance);
            Assert.Equal(Expression.And(inputExpression.ResourceExpressions).ToString(), visitedExpression.TableExpressions[0].NormalizedPredicate.ToString());
        }

        [Theory]
        [InlineData(SearchParameterNames.ResourceType)]
        [InlineData(SqlSearchParameters.ResourceSurrogateIdParameterName)]
        public void GivenExpressionWithMultipleDenormalizedExpressions_WhenRewritten_DenormalisedPredicatesClearedAndReplacedWithAllExpression(string paramName)
        {
            var inputExpression = new SqlRootExpression(
                new List<TableExpression>
                {
                    new TableExpression(null, new SearchParameterExpression(new SearchParameterInfo("myParam"), Expression.Equals(FieldName.String, null, "foo")), TableExpressionKind.Normal),
                },
                new List<SearchParameterExpressionBase>
                {
                    new SearchParameterExpression(new SearchParameterInfo(paramName), Expression.Equals(FieldName.String, null, "ExtractableTestParamValue")),
                    new SearchParameterExpression(new SearchParameterInfo(SearchParameterNames.Id), Expression.Equals(FieldName.String, null, "myid")),
                });
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(DenormalizedPredicateRewriter.Instance);
            Assert.Empty(visitedExpression.ResourceExpressions);
            Assert.Equal(new TableExpression(null, Expression.And(inputExpression.ResourceExpressions[0], inputExpression.ResourceExpressions[1]), TableExpressionKind.All).ToString(), visitedExpression.TableExpressions[0].ToString());
        }

        [Fact]
        public void GivenSqlRootExpressionWithDenormalizedPredicateAndOnlyIncludeTableExpression_WhenRewritten_DenormalizedPredicateIsPreserved()
        {
            var inputExpression = new SqlRootExpression(
                new List<TableExpression>
                {
                    new TableExpression(null, null, TableExpressionKind.Include),
                },
                new List<SearchParameterExpressionBase>
                {
                    new SearchParameterExpression(new SearchParameterInfo(SearchParameterNames.ResourceType), Expression.Equals(FieldName.String, null, "TestParamValue1")),
                });
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(DenormalizedPredicateRewriter.Instance);
            Assert.Same(inputExpression.ResourceExpressions, visitedExpression.ResourceExpressions);
        }

        [Theory]
        [InlineData(SearchParameterNames.ResourceType)]
        [InlineData(SqlSearchParameters.ResourceSurrogateIdParameterName)]
        public void GivenExpressionWithDenormalizedAndChainedExpressions_WhenRewritten_DenormalisedPredicatesPromotedToChainTableExpression(string paramName)
        {
            var inputExpression = new SqlRootExpression(
                new List<TableExpression>
                {
                    new TableExpression(
                        ChainAnchorQueryGenerator.Instance,
                        new SqlChainLinkExpression("Observation", new SearchParameterInfo("myref"), "Patient", false),
                        TableExpressionKind.Chain,
                        1),
                    new TableExpression(
                        null,
                        new SearchParameterExpression(new SearchParameterInfo("myParam"), Expression.Equals(FieldName.String, null, "foo")),
                        TableExpressionKind.Normal,
                        1),
                },
                new List<SearchParameterExpressionBase>
                {
                    new SearchParameterExpression(new SearchParameterInfo(paramName), Expression.Equals(FieldName.String, null, "ExtractableTestParamValue")),
                });
            var visitedExpression = (SqlRootExpression)inputExpression.AcceptVisitor(DenormalizedPredicateRewriter.Instance);
            Assert.Equal(inputExpression.ResourceExpressions[0], ((SqlChainLinkExpression)visitedExpression.TableExpressions[0].NormalizedPredicate).ExpressionOnSource);
            Assert.Equal(inputExpression.TableExpressions[0].ChainLevel, visitedExpression.TableExpressions[0].ChainLevel);
            Assert.Same(inputExpression.TableExpressions[1], visitedExpression.TableExpressions[1]);
        }
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    /// <summary>
    /// Unit tests for ChainFlatteningRewriter.
    /// These tests verify the rewriter's behavior using non-chain expressions to avoid ModelInfoProvider dependencies.
    /// The ChainFlatteningRewriter is more comprehensively tested through integration tests where
    /// the full FHIR stack (including ModelInfoProvider) is initialized.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ChainFlatteningRewriterTests
    {
        private readonly ChainFlatteningRewriter _rewriter;

        public ChainFlatteningRewriterTests()
        {
            var searchParamTypeMap = new SearchParameterToSearchValueTypeMap();
            var queryGeneratorFactory = new SearchParamTableExpressionQueryGeneratorFactory(searchParamTypeMap);
            _rewriter = new ChainFlatteningRewriter(queryGeneratorFactory);
        }

        [Fact]
        public void GivenASqlRootExpressionWithoutChainExpressions_WhenVisited_ThenSameExpressionIsReturned()
        {
            // Arrange - Expression with no chain expressions
            var normalExpression = Expression.Equals(FieldName.TokenCode, null, "code123");
            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, normalExpression, SearchParamTableExpressionKind.Normal),
                new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
            };

            var sqlRootExpression = new SqlRootExpression(tableExpressions, new List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRootExpression.AcceptVisitor(_rewriter, null);

            // Assert - Should return same expression since there are no chains
            Assert.Same(sqlRootExpression, result);
            Assert.Equal(2, result.SearchParamTableExpressions.Count);
        }

        [Fact]
        public void GivenEmptySqlRootExpression_WhenVisited_ThenSameExpressionIsReturned()
        {
            // Arrange
            var tableExpressions = new List<SearchParamTableExpression>();
            var sqlRootExpression = new SqlRootExpression(tableExpressions, new List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRootExpression.AcceptVisitor(_rewriter, null);

            // Assert - Empty list means no modifications needed
            Assert.Same(sqlRootExpression, result);
        }

        [Fact]
        public void GivenSqlRootWithMultipleNonChainExpressions_WhenVisited_ThenAllArePreserved()
        {
            // Arrange
            var expr1 = Expression.Equals(FieldName.TokenCode, null, "code1");
            var expr2 = Expression.Equals(FieldName.String, null, "value2");
            var expr3 = Expression.GreaterThan(FieldName.Number, null, 10);

            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, expr1, SearchParamTableExpressionKind.Normal),
                new SearchParamTableExpression(null, expr2, SearchParamTableExpressionKind.Normal),
                new SearchParamTableExpression(null, expr3, SearchParamTableExpressionKind.Normal),
            };

            var sqlRootExpression = new SqlRootExpression(tableExpressions, new List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRootExpression.AcceptVisitor(_rewriter, null);

            // Assert - All expressions should be preserved as-is
            Assert.Same(sqlRootExpression, result);
            Assert.Equal(3, result.SearchParamTableExpressions.Count);
            Assert.Same(expr1, result.SearchParamTableExpressions[0].Predicate);
            Assert.Same(expr2, result.SearchParamTableExpressions[1].Predicate);
            Assert.Same(expr3, result.SearchParamTableExpressions[2].Predicate);
        }

        [Fact]
        public void GivenSqlRootWithTopExpression_WhenVisited_ThenTopIsPreserved()
        {
            // Arrange
            var normalExpr = Expression.Equals(FieldName.TokenCode, null, "test");

            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, normalExpr, SearchParamTableExpressionKind.Normal),
                new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
            };

            var sqlRootExpression = new SqlRootExpression(tableExpressions, new List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRootExpression.AcceptVisitor(_rewriter, null);

            // Assert - Top expression should be unchanged
            Assert.Same(sqlRootExpression, result);
            Assert.Equal(SearchParamTableExpressionKind.Top, result.SearchParamTableExpressions[1].Kind);
        }

        [Fact]
        public void GivenSqlRootWithResourceTableExpressions_WhenVisited_ThenResourceTableExpressionsArePreserved()
        {
            // Arrange
            var tableExpression = new SearchParamTableExpression(null, Expression.Equals(FieldName.TokenCode, null, "test"), SearchParamTableExpressionKind.Normal);
            var resourceTableExpression = Expression.SearchParameter(new SearchParameterInfo("_type", "_type"), Expression.Equals(FieldName.TokenCode, null, "Patient"));

            var sqlRootExpression = new SqlRootExpression(
                new List<SearchParamTableExpression> { tableExpression },
                new List<SearchParameterExpressionBase> { resourceTableExpression });

            // Act
            var result = (SqlRootExpression)sqlRootExpression.AcceptVisitor(_rewriter, null);

            // Assert - ResourceTableExpressions should be preserved
            Assert.Same(sqlRootExpression, result);
            Assert.Single(result.ResourceTableExpressions);
            Assert.Same(resourceTableExpression, result.ResourceTableExpressions[0]);
        }

        [Fact]
        public void GivenRewriterInstance_WhenCreated_ThenNotNull()
        {
            // Assert - Verify rewriter was created successfully
            Assert.NotNull(_rewriter);
        }

        [Fact]
        public void GivenSqlRootWithAllExpression_WhenVisited_ThenAllExpressionPreserved()
        {
            // Arrange
            var allExpression = Expression.Equals(FieldName.TokenCode, null, "all-test");

            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, allExpression, SearchParamTableExpressionKind.All),
            };

            var sqlRootExpression = new SqlRootExpression(tableExpressions, new List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRootExpression.AcceptVisitor(_rewriter, null);

            // Assert
            Assert.Same(sqlRootExpression, result);
            Assert.Equal(SearchParamTableExpressionKind.All, result.SearchParamTableExpressions[0].Kind);
        }

        [Fact]
        public void GivenSqlRootWithIncludeExpression_WhenVisited_ThenIncludeExpressionPreserved()
        {
            // Arrange
            var includeExpression = Expression.Equals(FieldName.String, null, "include-test");

            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, includeExpression, SearchParamTableExpressionKind.Include),
            };

            var sqlRootExpression = new SqlRootExpression(tableExpressions, new List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRootExpression.AcceptVisitor(_rewriter, null);

            // Assert - Non-chain expressions should pass through unchanged
            Assert.Same(sqlRootExpression, result);
            Assert.Equal(SearchParamTableExpressionKind.Include, result.SearchParamTableExpressions[0].Kind);
        }
    }
}

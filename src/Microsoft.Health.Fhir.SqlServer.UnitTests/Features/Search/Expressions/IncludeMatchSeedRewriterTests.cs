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
    /// <summary>
    /// Unit tests for IncludeMatchSeedRewriter.
    /// Tests the rewriter's ability to add an All SearchParamTableExpression as a seed for match results
    /// when SearchParamTableExpressions consist solely of Include expressions.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class IncludeMatchSeedRewriterTests
    {
        [Fact]
        public void GivenOnlyIncludeExpressions_WhenRewritten_ThenAllExpressionIsAdded()
        {
            // Arrange - Query like: Observation?_include=Observation:subject
            var includeExpression = Expression.Equals(FieldName.ReferenceResourceType, null, "Patient");
            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, includeExpression, SearchParamTableExpressionKind.Include),
            };

            var resourceTableExpression = Expression.SearchParameter(
                new SearchParameterInfo("_type", "_type"),
                Expression.Equals(FieldName.TokenCode, null, "Observation"));

            var sqlRoot = new SqlRootExpression(tableExpressions, new List<SearchParameterExpressionBase> { resourceTableExpression });

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(IncludeMatchSeedRewriter.Instance, null);

            // Assert - Should add an All expression at the beginning
            Assert.NotNull(result);
            Assert.Equal(2, result.SearchParamTableExpressions.Count);

            // First expression should be All (seed)
            Assert.Equal(SearchParamTableExpressionKind.All, result.SearchParamTableExpressions[0].Kind);

            // Second should be the original Include
            Assert.Equal(SearchParamTableExpressionKind.Include, result.SearchParamTableExpressions[1].Kind);
            Assert.Same(includeExpression, result.SearchParamTableExpressions[1].Predicate);

            // ResourceTableExpressions should be cleared and moved to All expression
            Assert.Empty(result.ResourceTableExpressions);
        }

        [Fact]
        public void GivenMultipleIncludeExpressions_WhenRewritten_ThenAllExpressionAddedBeforeAll()
        {
            // Arrange - Multiple includes with a resource expression to avoid validation error
            var include1 = Expression.Equals(FieldName.ReferenceResourceType, null, "Patient");
            var include2 = Expression.Equals(FieldName.ReferenceResourceType, null, "Practitioner");

            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, include1, SearchParamTableExpressionKind.Include),
                new SearchParamTableExpression(null, include2, SearchParamTableExpressionKind.Include),
            };

            var resourceExpr = Expression.SearchParameter(
                new SearchParameterInfo("_type", "_type"),
                Expression.Equals(FieldName.TokenCode, null, "Observation"));

            var sqlRoot = new SqlRootExpression(tableExpressions, new List<SearchParameterExpressionBase> { resourceExpr });

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(IncludeMatchSeedRewriter.Instance, null);

            // Assert
            Assert.Equal(3, result.SearchParamTableExpressions.Count);

            // First should be All
            Assert.Equal(SearchParamTableExpressionKind.All, result.SearchParamTableExpressions[0].Kind);

            // Remaining should be the includes in order
            Assert.Equal(SearchParamTableExpressionKind.Include, result.SearchParamTableExpressions[1].Kind);
            Assert.Same(include1, result.SearchParamTableExpressions[1].Predicate);

            Assert.Equal(SearchParamTableExpressionKind.Include, result.SearchParamTableExpressions[2].Kind);
            Assert.Same(include2, result.SearchParamTableExpressions[2].Predicate);
        }

        [Fact]
        public void GivenMixedIncludeAndNormalExpressions_WhenRewritten_ThenNoRewriteOccurs()
        {
            // Arrange - Mix of Include and Normal (like: Observation?code=abc&_include=Observation:subject)
            var normalExpression = Expression.Equals(FieldName.TokenCode, null, "abc");
            var includeExpression = Expression.Equals(FieldName.ReferenceResourceType, null, "Patient");

            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, normalExpression, SearchParamTableExpressionKind.Normal),
                new SearchParamTableExpression(null, includeExpression, SearchParamTableExpressionKind.Include),
            };

            var sqlRoot = new SqlRootExpression(tableExpressions, new List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(IncludeMatchSeedRewriter.Instance, null);

            // Assert - Should return unchanged
            Assert.Same(sqlRoot, result);
            Assert.Equal(2, result.SearchParamTableExpressions.Count);
        }

        [Fact]
        public void GivenNormalExpressionsOnly_WhenRewritten_ThenNoRewriteOccurs()
        {
            // Arrange - No includes
            var expression = Expression.Equals(FieldName.TokenCode, null, "test");
            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, expression, SearchParamTableExpressionKind.Normal),
            };

            var sqlRoot = new SqlRootExpression(tableExpressions, new List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(IncludeMatchSeedRewriter.Instance, null);

            // Assert
            Assert.Same(sqlRoot, result);
            Assert.Single(result.SearchParamTableExpressions);
        }

        [Fact]
        public void GivenEmptySqlRoot_WhenRewritten_ThenReturnsUnchanged()
        {
            // Arrange
            var sqlRoot = new SqlRootExpression(
                new List<SearchParamTableExpression>(),
                new List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(IncludeMatchSeedRewriter.Instance, null);

            // Assert
            Assert.Same(sqlRoot, result);
        }

        [Fact]
        public void GivenIncludeWithMultipleResourceTableExpressions_WhenRewritten_ThenResourceExpressionsAreCombined()
        {
            // Arrange
            var includeExpression = Expression.Equals(FieldName.ReferenceResourceType, null, "Patient");
            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, includeExpression, SearchParamTableExpressionKind.Include),
            };

            var resourceExpr1 = Expression.SearchParameter(
                new SearchParameterInfo("_type", "_type"),
                Expression.Equals(FieldName.TokenCode, null, "Observation"));

            var resourceExpr2 = Expression.SearchParameter(
                new SearchParameterInfo("_lastUpdated", "_lastUpdated"),
                Expression.GreaterThan(FieldName.DateTimeStart, null, System.DateTimeOffset.UtcNow));

            var resourceTableExpressions = new List<SearchParameterExpressionBase> { resourceExpr1, resourceExpr2 };

            var sqlRoot = new SqlRootExpression(tableExpressions, resourceTableExpressions);

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(IncludeMatchSeedRewriter.Instance, null);

            // Assert
            Assert.Equal(2, result.SearchParamTableExpressions.Count);
            Assert.Equal(SearchParamTableExpressionKind.All, result.SearchParamTableExpressions[0].Kind);

            // The All expression should contain an And combining the resource expressions
            var allPredicate = result.SearchParamTableExpressions[0].Predicate;
            Assert.IsType<MultiaryExpression>(allPredicate);
            var andExpression = (MultiaryExpression)allPredicate;
            Assert.Equal(MultiaryOperator.And, andExpression.MultiaryOperation);
            Assert.Equal(2, andExpression.Expressions.Count);
        }

        [Fact]
        public void GivenIncludeWithSingleResourceTableExpression_WhenRewritten_ThenResourceExpressionWrappedInAnd()
        {
            // Arrange
            var includeExpression = Expression.Equals(FieldName.ReferenceResourceType, null, "Patient");
            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, includeExpression, SearchParamTableExpressionKind.Include),
            };

            var resourceExpr = Expression.SearchParameter(
                new SearchParameterInfo("_type", "_type"),
                Expression.Equals(FieldName.TokenCode, null, "Observation"));

            var sqlRoot = new SqlRootExpression(tableExpressions, new List<SearchParameterExpressionBase> { resourceExpr });

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(IncludeMatchSeedRewriter.Instance, null);

            // Assert - Expression.And() always creates And expression even with single element
            var allPredicate = result.SearchParamTableExpressions[0].Predicate;
            var andExpression = Assert.IsType<MultiaryExpression>(allPredicate);
            Assert.Equal(MultiaryOperator.And, andExpression.MultiaryOperation);
            Assert.Single(andExpression.Expressions);
            Assert.Same(resourceExpr, andExpression.Expressions[0]);
        }

        [Fact]
        public void GivenIncludeWithChainExpression_WhenRewritten_ThenNoRewriteOccurs()
        {
            // Arrange - Include with Chain
            var chainExpression = Expression.Equals(FieldName.String, null, "chain-test");
            var includeExpression = Expression.Equals(FieldName.ReferenceResourceType, null, "Patient");

            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, chainExpression, SearchParamTableExpressionKind.Chain),
                new SearchParamTableExpression(null, includeExpression, SearchParamTableExpressionKind.Include),
            };

            var resourceExpr = Expression.SearchParameter(
                new SearchParameterInfo("_type", "_type"),
                Expression.Equals(FieldName.TokenCode, null, "Observation"));

            var sqlRoot = new SqlRootExpression(tableExpressions, new List<SearchParameterExpressionBase> { resourceExpr });

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(IncludeMatchSeedRewriter.Instance, null);

            // Assert - Mixed types, no rewrite
            Assert.Same(sqlRoot, result);
            Assert.Equal(2, result.SearchParamTableExpressions.Count);
        }

        [Fact]
        public void GivenIncludeWithTopExpression_WhenRewritten_ThenNoRewriteOccurs()
        {
            // Arrange
            var includeExpression = Expression.Equals(FieldName.ReferenceResourceType, null, "Patient");

            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, includeExpression, SearchParamTableExpressionKind.Include),
                new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
            };

            var resourceExpr = Expression.SearchParameter(
                new SearchParameterInfo("_type", "_type"),
                Expression.Equals(FieldName.TokenCode, null, "Observation"));

            var sqlRoot = new SqlRootExpression(tableExpressions, new List<SearchParameterExpressionBase> { resourceExpr });

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(IncludeMatchSeedRewriter.Instance, null);

            // Assert - Mixed types, no rewrite
            Assert.Same(sqlRoot, result);
            Assert.Equal(2, result.SearchParamTableExpressions.Count);
        }

        [Fact]
        public void GivenIncludeWithSortExpression_WhenRewritten_ThenNoRewriteOccurs()
        {
            // Arrange
            var sortExpression = Expression.Equals(FieldName.DateTimeStart, null, System.DateTimeOffset.UtcNow);
            var includeExpression = Expression.Equals(FieldName.ReferenceResourceType, null, "Patient");

            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, sortExpression, SearchParamTableExpressionKind.Sort),
                new SearchParamTableExpression(null, includeExpression, SearchParamTableExpressionKind.Include),
            };

            var resourceExpr = Expression.SearchParameter(
                new SearchParameterInfo("_type", "_type"),
                Expression.Equals(FieldName.TokenCode, null, "Observation"));

            var sqlRoot = new SqlRootExpression(tableExpressions, new List<SearchParameterExpressionBase> { resourceExpr });

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(IncludeMatchSeedRewriter.Instance, null);

            // Assert
            Assert.Same(sqlRoot, result);
            Assert.Equal(2, result.SearchParamTableExpressions.Count);
        }

        [Fact]
        public void GivenIncludeWithAllExpression_WhenRewritten_ThenNoRewriteOccurs()
        {
            // Arrange - Already has All expression
            var allExpression = Expression.Equals(FieldName.TokenCode, null, "all-test");
            var includeExpression = Expression.Equals(FieldName.ReferenceResourceType, null, "Patient");

            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, allExpression, SearchParamTableExpressionKind.All),
                new SearchParamTableExpression(null, includeExpression, SearchParamTableExpressionKind.Include),
            };

            var resourceExpr = Expression.SearchParameter(
                new SearchParameterInfo("_type", "_type"),
                Expression.Equals(FieldName.TokenCode, null, "Observation"));

            var sqlRoot = new SqlRootExpression(tableExpressions, new List<SearchParameterExpressionBase> { resourceExpr });

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(IncludeMatchSeedRewriter.Instance, null);

            // Assert - Mixed types, no rewrite
            Assert.Same(sqlRoot, result);
            Assert.Equal(2, result.SearchParamTableExpressions.Count);
        }

        [Fact]
        public void GivenThreeIncludeExpressions_WhenRewritten_ThenAllAddedAndOrderPreserved()
        {
            // Arrange
            var include1 = Expression.Equals(FieldName.ReferenceResourceType, null, "Patient");
            var include2 = Expression.Equals(FieldName.ReferenceResourceType, null, "Practitioner");
            var include3 = Expression.Equals(FieldName.ReferenceResourceType, null, "Organization");

            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, include1, SearchParamTableExpressionKind.Include),
                new SearchParamTableExpression(null, include2, SearchParamTableExpressionKind.Include),
                new SearchParamTableExpression(null, include3, SearchParamTableExpressionKind.Include),
            };

            var resourceExpr = Expression.SearchParameter(
                new SearchParameterInfo("_type", "_type"),
                Expression.Equals(FieldName.TokenCode, null, "Observation"));

            var sqlRoot = new SqlRootExpression(tableExpressions, new List<SearchParameterExpressionBase> { resourceExpr });

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(IncludeMatchSeedRewriter.Instance, null);

            // Assert
            Assert.Equal(4, result.SearchParamTableExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.All, result.SearchParamTableExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Include, result.SearchParamTableExpressions[1].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Include, result.SearchParamTableExpressions[2].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Include, result.SearchParamTableExpressions[3].Kind);

            // Verify order is preserved
            Assert.Same(include1, result.SearchParamTableExpressions[1].Predicate);
            Assert.Same(include2, result.SearchParamTableExpressions[2].Predicate);
            Assert.Same(include3, result.SearchParamTableExpressions[3].Predicate);
        }

        [Fact]
        public void GivenIncludeWithConcatenationExpression_WhenRewritten_ThenNoRewriteOccurs()
        {
            // Arrange - Include with Concatenation
            var concatenationExpression = Expression.Equals(FieldName.Number, null, 42);
            var includeExpression = Expression.Equals(FieldName.ReferenceResourceType, null, "Patient");

            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, concatenationExpression, SearchParamTableExpressionKind.Concatenation),
                new SearchParamTableExpression(null, includeExpression, SearchParamTableExpressionKind.Include),
            };

            var resourceExpr = Expression.SearchParameter(
                new SearchParameterInfo("_type", "_type"),
                Expression.Equals(FieldName.TokenCode, null, "Observation"));

            var sqlRoot = new SqlRootExpression(tableExpressions, new List<SearchParameterExpressionBase> { resourceExpr });

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(IncludeMatchSeedRewriter.Instance, null);

            // Assert
            Assert.Same(sqlRoot, result);
            Assert.Equal(2, result.SearchParamTableExpressions.Count);
        }

        [Fact]
        public void GivenOnlyIncludesWithComplexResourceExpression_WhenRewritten_ThenResourceExpressionCombinedInAnd()
        {
            // Arrange
            var includeExpression = Expression.Equals(FieldName.ReferenceResourceType, null, "Patient");
            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, includeExpression, SearchParamTableExpressionKind.Include),
            };

            // Complex resource expression with Or
            var typeExpr = Expression.SearchParameter(
                new SearchParameterInfo("_type", "_type"),
                Expression.Or(
                    Expression.Equals(FieldName.TokenCode, null, "Observation"),
                    Expression.Equals(FieldName.TokenCode, null, "Condition")));

            var sqlRoot = new SqlRootExpression(tableExpressions, new List<SearchParameterExpressionBase> { typeExpr });

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(IncludeMatchSeedRewriter.Instance, null);

            // Assert
            Assert.Equal(2, result.SearchParamTableExpressions.Count);
            Assert.Equal(SearchParamTableExpressionKind.All, result.SearchParamTableExpressions[0].Kind);

            // Expression.And() wraps even single expressions
            var allPredicate = result.SearchParamTableExpressions[0].Predicate;
            var andExpression = Assert.IsType<MultiaryExpression>(allPredicate);
            Assert.Equal(MultiaryOperator.And, andExpression.MultiaryOperation);
            Assert.Single(andExpression.Expressions);
            Assert.Same(typeExpr, andExpression.Expressions[0]);
        }

        [Fact]
        public void GivenRewriterInstance_WhenAccessed_ThenNotNull()
        {
            // Assert - Verify singleton instance exists
            Assert.NotNull(IncludeMatchSeedRewriter.Instance);
        }
    }
}

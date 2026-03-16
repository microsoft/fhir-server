// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
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
    /// Unit tests for IncludesOperationRewriter.
    /// Tests the rewriter's ability to reorder include expressions for the $includes operation,
    /// adding IncludeUnionAll and IncludeLimit expressions at the end.
    /// Key difference from IncludeRewriter: Does NOT add IncludeLimit after each include expression.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class IncludesOperationRewriterTests
    {
        [Fact]
        public void GivenNullExpression_WhenRewritten_ThenReturnsNull()
        {
            // Act
            var result = IncludesOperationRewriter.Instance.VisitSqlRoot(null, null);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void GivenSingleExpression_WhenRewritten_ThenReturnsUnchanged()
        {
            // Arrange - Only one expression
            var normalExpression = Expression.Equals(FieldName.TokenCode, null, "test");
            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, normalExpression, SearchParamTableExpressionKind.Normal),
            };

            var sqlRoot = new SqlRootExpression(tableExpressions, new List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(IncludesOperationRewriter.Instance, null);

            // Assert - Should return unchanged (count == 1)
            Assert.Same(sqlRoot, result);
        }

        [Fact]
        public void GivenNoIncludeExpressions_WhenRewritten_ThenReturnsUnchanged()
        {
            // Arrange - No include expressions
            var expr1 = Expression.Equals(FieldName.TokenCode, null, "code1");
            var expr2 = Expression.Equals(FieldName.String, null, "value2");

            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, expr1, SearchParamTableExpressionKind.Normal),
                new SearchParamTableExpression(null, expr2, SearchParamTableExpressionKind.Normal),
            };

            var sqlRoot = new SqlRootExpression(tableExpressions, new List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(IncludesOperationRewriter.Instance, null);

            // Assert - No includes, should return unchanged
            Assert.Same(sqlRoot, result);
            Assert.Equal(2, result.SearchParamTableExpressions.Count);
        }

        [Fact]
        public void GivenMixedExpressionsWithIncludes_WhenRewritten_ThenIncludesMovedToEnd()
        {
            // Arrange - Normal expression followed by include
            var normalExpr = Expression.Equals(FieldName.TokenCode, null, "test");
            var includeExpr = CreateIncludeExpression("Patient", "Observation", "subject", reversed: false, iterate: false);

            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, normalExpr, SearchParamTableExpressionKind.Normal),
                new SearchParamTableExpression(null, includeExpr, SearchParamTableExpressionKind.Include),
            };

            var sqlRoot = new SqlRootExpression(tableExpressions, new List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(IncludesOperationRewriter.Instance, null);

            // Assert - Should reorder: Normal, Include, IncludeUnionAll, IncludeLimit
            Assert.Equal(4, result.SearchParamTableExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.Normal, result.SearchParamTableExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Include, result.SearchParamTableExpressions[1].Kind);
            Assert.Equal(SearchParamTableExpressionKind.IncludeUnionAll, result.SearchParamTableExpressions[2].Kind);
            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, result.SearchParamTableExpressions[3].Kind);
        }

        [Fact]
        public void GivenIncludesOperationRewriter_WhenProcessingIncludes_ThenNoLimitAfterEachInclude()
        {
            // Arrange - Multiple includes (key difference from base IncludeRewriter)
            var include1 = CreateIncludeExpression("Patient", "Observation", "subject", reversed: false, iterate: false);
            var include2 = CreateIncludeExpression("Patient", "Practitioner", "general-practitioner", reversed: false, iterate: false);

            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, include1, SearchParamTableExpressionKind.Include),
                new SearchParamTableExpression(null, include2, SearchParamTableExpressionKind.Include),
            };

            var sqlRoot = new SqlRootExpression(tableExpressions, new List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(IncludesOperationRewriter.Instance, null);

            // Assert - IncludesOperationRewriter should NOT add IncludeLimit after each include
            // Expected: Include1, Include2, IncludeUnionAll, IncludeLimit (4 total, not 6)
            Assert.Equal(4, result.SearchParamTableExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.Include, result.SearchParamTableExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Include, result.SearchParamTableExpressions[1].Kind);
            Assert.Equal(SearchParamTableExpressionKind.IncludeUnionAll, result.SearchParamTableExpressions[2].Kind);
            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, result.SearchParamTableExpressions[3].Kind);
        }

        [Fact]
        public void GivenIncludeIterateExpressions_WhenRewritten_ThenSortedCorrectly()
        {
            // Arrange - Include iterate should come after regular include
            var include = CreateIncludeExpression("Patient", "Observation", "subject", reversed: false, iterate: false);
            var includeIterate = CreateIncludeExpression("Observation", "Practitioner", "performer", reversed: false, iterate: true, sourceTypeOverrideForIterate: "Observation");

            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, includeIterate, SearchParamTableExpressionKind.Include), // Out of order
                new SearchParamTableExpression(null, include, SearchParamTableExpressionKind.Include),
            };

            var sqlRoot = new SqlRootExpression(tableExpressions, new List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(IncludesOperationRewriter.Instance, null);

            // Assert - Include iterate should be sorted after regular include
            Assert.Equal(4, result.SearchParamTableExpressions.Count);

            // Regular include should come first
            Assert.Equal(SearchParamTableExpressionKind.Include, result.SearchParamTableExpressions[0].Kind);
            var firstInclude = (IncludeExpression)result.SearchParamTableExpressions[0].Predicate;
            Assert.False(firstInclude.Iterate);

            // Include iterate should come second
            Assert.Equal(SearchParamTableExpressionKind.Include, result.SearchParamTableExpressions[1].Kind);
            var secondInclude = (IncludeExpression)result.SearchParamTableExpressions[1].Predicate;
            Assert.True(secondInclude.Iterate);

            Assert.Equal(SearchParamTableExpressionKind.IncludeUnionAll, result.SearchParamTableExpressions[2].Kind);
            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, result.SearchParamTableExpressions[3].Kind);
        }

        [Fact]
        public void GivenNormalAndIncludeExpressions_WhenRewritten_ThenNormalComesFirst()
        {
            // Arrange - Include before normal (should be reordered)
            var includeExpr = CreateIncludeExpression("Patient", "Observation", "subject", reversed: false, iterate: false);
            var normalExpr = Expression.Equals(FieldName.TokenCode, null, "test");

            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, includeExpr, SearchParamTableExpressionKind.Include),
                new SearchParamTableExpression(null, normalExpr, SearchParamTableExpressionKind.Normal),
            };

            var sqlRoot = new SqlRootExpression(tableExpressions, new List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(IncludesOperationRewriter.Instance, null);

            // Assert - Normal should be reordered to come first
            Assert.Equal(4, result.SearchParamTableExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.Normal, result.SearchParamTableExpressions[0].Kind);
            Assert.Same(normalExpr, result.SearchParamTableExpressions[0].Predicate);

            Assert.Equal(SearchParamTableExpressionKind.Include, result.SearchParamTableExpressions[1].Kind);
            Assert.Same(includeExpr, result.SearchParamTableExpressions[1].Predicate);
        }

        [Fact]
        public void GivenMultipleNormalAndIncludeExpressions_WhenRewritten_ThenCorrectOrder()
        {
            // Arrange - Mix of normal and include expressions
            var normal1 = Expression.Equals(FieldName.TokenCode, null, "code1");
            var include1 = CreateIncludeExpression("Patient", "Observation", "subject", reversed: false, iterate: false);
            var normal2 = Expression.Equals(FieldName.String, null, "value2");
            var include2 = CreateIncludeExpression("Patient", "Practitioner", "general-practitioner", reversed: false, iterate: false);

            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, include1, SearchParamTableExpressionKind.Include),
                new SearchParamTableExpression(null, normal1, SearchParamTableExpressionKind.Normal),
                new SearchParamTableExpression(null, include2, SearchParamTableExpressionKind.Include),
                new SearchParamTableExpression(null, normal2, SearchParamTableExpressionKind.Normal),
            };

            var sqlRoot = new SqlRootExpression(tableExpressions, new List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(IncludesOperationRewriter.Instance, null);

            // Assert - All normals first, then all includes, then union/limit
            Assert.Equal(6, result.SearchParamTableExpressions.Count);

            // Normals
            Assert.Equal(SearchParamTableExpressionKind.Normal, result.SearchParamTableExpressions[0].Kind);
            Assert.Same(normal1, result.SearchParamTableExpressions[0].Predicate);

            Assert.Equal(SearchParamTableExpressionKind.Normal, result.SearchParamTableExpressions[1].Kind);
            Assert.Same(normal2, result.SearchParamTableExpressions[1].Predicate);

            // Includes
            Assert.Equal(SearchParamTableExpressionKind.Include, result.SearchParamTableExpressions[2].Kind);
            Assert.Same(include1, result.SearchParamTableExpressions[2].Predicate);

            Assert.Equal(SearchParamTableExpressionKind.Include, result.SearchParamTableExpressions[3].Kind);
            Assert.Same(include2, result.SearchParamTableExpressions[3].Predicate);

            // Union and Limit
            Assert.Equal(SearchParamTableExpressionKind.IncludeUnionAll, result.SearchParamTableExpressions[4].Kind);
            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, result.SearchParamTableExpressions[5].Kind);
        }

        [Fact]
        public void GivenChainExpression_WhenRewritten_ThenChainPreservedBeforeIncludes()
        {
            // Arrange - Chain and include expressions
            var chainExpr = Expression.Equals(FieldName.String, null, "chain");
            var includeExpr = CreateIncludeExpression("Patient", "Observation", "subject", reversed: false, iterate: false);

            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, includeExpr, SearchParamTableExpressionKind.Include),
                new SearchParamTableExpression(null, chainExpr, SearchParamTableExpressionKind.Chain),
            };

            var sqlRoot = new SqlRootExpression(tableExpressions, new List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(IncludesOperationRewriter.Instance, null);

            // Assert - Chain should come before include
            Assert.Equal(4, result.SearchParamTableExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.Chain, result.SearchParamTableExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Include, result.SearchParamTableExpressions[1].Kind);
            Assert.Equal(SearchParamTableExpressionKind.IncludeUnionAll, result.SearchParamTableExpressions[2].Kind);
            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, result.SearchParamTableExpressions[3].Kind);
        }

        [Fact]
        public void GivenTopExpression_WhenRewritten_ThenTopPreservedBeforeIncludes()
        {
            // Arrange
            var includeExpr = CreateIncludeExpression("Patient", "Observation", "subject", reversed: false, iterate: false);

            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, includeExpr, SearchParamTableExpressionKind.Include),
                new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
            };

            var sqlRoot = new SqlRootExpression(tableExpressions, new List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(IncludesOperationRewriter.Instance, null);

            // Assert - Top should come before include
            Assert.Equal(4, result.SearchParamTableExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.Top, result.SearchParamTableExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Include, result.SearchParamTableExpressions[1].Kind);
        }

        [Fact]
        public void GivenSortExpression_WhenRewritten_ThenSortPreservedBeforeIncludes()
        {
            // Arrange
            var sortExpr = Expression.Equals(FieldName.DateTimeStart, null, System.DateTimeOffset.UtcNow);
            var includeExpr = CreateIncludeExpression("Patient", "Observation", "subject", reversed: false, iterate: false);

            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, includeExpr, SearchParamTableExpressionKind.Include),
                new SearchParamTableExpression(null, sortExpr, SearchParamTableExpressionKind.Sort),
            };

            var sqlRoot = new SqlRootExpression(tableExpressions, new List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(IncludesOperationRewriter.Instance, null);

            // Assert - Sort should come before include
            Assert.Equal(4, result.SearchParamTableExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.Sort, result.SearchParamTableExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Include, result.SearchParamTableExpressions[1].Kind);
        }

        [Fact]
        public void GivenResourceTableExpressions_WhenRewritten_ThenResourceExpressionsPreserved()
        {
            // Arrange
            var includeExpr = CreateIncludeExpression("Patient", "Observation", "subject", reversed: false, iterate: false);
            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, includeExpr, SearchParamTableExpressionKind.Include),
            };

            var resourceExpr = Expression.SearchParameter(
                new SearchParameterInfo("_type", "_type"),
                Expression.Equals(FieldName.TokenCode, null, "Patient"));

            var sqlRoot = new SqlRootExpression(tableExpressions, new List<SearchParameterExpressionBase> { resourceExpr });

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(IncludesOperationRewriter.Instance, null);

            // Assert - Resource table expressions should be preserved
            Assert.Single(result.ResourceTableExpressions);
            Assert.Same(resourceExpr, result.ResourceTableExpressions[0]);
        }

        [Fact]
        public void GivenComplexIncludeIterateDependencies_WhenRewritten_ThenSortedByDependency()
        {
            // Arrange - Create includes with dependencies: Observation -> Device -> Location
            var include1 = CreateIncludeExpression("Patient", "Observation", "subject", reversed: false, iterate: false);
            var includeIterate1 = CreateIncludeExpression("Observation", "Device", "device", reversed: false, iterate: true, sourceTypeOverrideForIterate: "Observation");
            var includeIterate2 = CreateIncludeExpression("Device", "Location", "location", reversed: false, iterate: true, sourceTypeOverrideForIterate: "Device");

            var tableExpressions = new List<SearchParamTableExpression>
            {
                // Add in wrong order
                new SearchParamTableExpression(null, includeIterate2, SearchParamTableExpressionKind.Include),
                new SearchParamTableExpression(null, include1, SearchParamTableExpressionKind.Include),
                new SearchParamTableExpression(null, includeIterate1, SearchParamTableExpressionKind.Include),
            };

            var sqlRoot = new SqlRootExpression(tableExpressions, new List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(IncludesOperationRewriter.Instance, null);

            // Assert - Should be sorted by dependency chain
            Assert.Equal(5, result.SearchParamTableExpressions.Count);

            // Regular include first
            var expr0 = (IncludeExpression)result.SearchParamTableExpressions[0].Predicate;
            Assert.False(expr0.Iterate);
            Assert.Equal("Observation", expr0.TargetResourceType);

            // Then iterate to Device (depends on Observation)
            var expr1 = (IncludeExpression)result.SearchParamTableExpressions[1].Predicate;
            Assert.True(expr1.Iterate);
            Assert.Equal("Device", expr1.TargetResourceType);

            // Then iterate to Location (depends on Device)
            var expr2 = (IncludeExpression)result.SearchParamTableExpressions[2].Predicate;
            Assert.True(expr2.Iterate);
            Assert.Equal("Location", expr2.TargetResourceType);
        }

        [Fact]
        public void GivenRewriterInstance_WhenAccessed_ThenNotNull()
        {
            // Assert - Verify singleton instance exists
            Assert.NotNull(IncludesOperationRewriter.Instance);
        }

        [Fact]
        public void GivenOnlyIncludeExpression_WhenRewritten_ThenAddsUnionAndLimit()
        {
            // Arrange - Only include, no other expressions
            var includeExpr = CreateIncludeExpression("Patient", "Observation", "subject", reversed: false, iterate: false);

            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, includeExpr, SearchParamTableExpressionKind.Include),
                new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Normal), // Need at least 2 for rewrite
            };

            var sqlRoot = new SqlRootExpression(tableExpressions, new List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(IncludesOperationRewriter.Instance, null);

            // Assert
            Assert.Equal(4, result.SearchParamTableExpressions.Count);
            Assert.Equal(SearchParamTableExpressionKind.IncludeUnionAll, result.SearchParamTableExpressions[2].Kind);
            Assert.Equal(SearchParamTableExpressionKind.IncludeLimit, result.SearchParamTableExpressions[3].Kind);
        }

        private static IncludeExpression CreateIncludeExpression(
            string sourceType,
            string targetType,
            string searchParameter,
            bool reversed,
            bool iterate,
            string sourceTypeOverrideForIterate = null)
        {
            var referenceSearchParam = new SearchParameterInfo(searchParameter, searchParameter)
            {
                Type = ValueSets.SearchParamType.Reference,
            };

            // For iterate expressions, use the source type override if provided
            string actualSourceType = iterate && sourceTypeOverrideForIterate != null ? sourceTypeOverrideForIterate : sourceType;

            return new IncludeExpression(
                new[] { actualSourceType },
                referenceSearchParam,
                actualSourceType,
                targetType,
                new[] { targetType },
                wildCard: false,
                iterate: iterate,
                reversed: reversed,
                allowedResourceTypesByScope: null);
        }
    }
}

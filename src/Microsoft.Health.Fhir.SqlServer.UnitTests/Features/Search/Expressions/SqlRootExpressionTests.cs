// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    /// <summary>
    /// Unit tests for SqlRootExpression.
    /// Tests business logic for constructor, helper methods, ValueInsensitiveEquals, and hash code generation.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SqlRootExpressionTests
    {
        private static readonly SearchParameterInfo TestSearchParam = new SearchParameterInfo(
            name: "status",
            code: "status",
            searchParamType: SearchParamType.Token,
            url: new Uri("http://hl7.org/fhir/SearchParameter/Resource-status"));

        [Fact]
        public void GivenValidParameters_WhenConstructed_ThenPropertiesAreSetCorrectly()
        {
            var searchParamTableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(TokenQueryGenerator.Instance, null, SearchParamTableExpressionKind.Normal),
            };

            var resourceTableExpressions = new List<SearchParameterExpressionBase>
            {
                new SearchParameterExpression(TestSearchParam, Expression.Equals(FieldName.TokenCode, null, "active")),
            };

            var expression = new SqlRootExpression(searchParamTableExpressions, resourceTableExpressions);

            Assert.Same(searchParamTableExpressions, expression.SearchParamTableExpressions);
            Assert.Same(resourceTableExpressions, expression.ResourceTableExpressions);
        }

        [Fact]
        public void GivenNullSearchParamTableExpressions_WhenConstructed_ThenThrowsArgumentNullException()
        {
            var resourceTableExpressions = new List<SearchParameterExpressionBase>();

            Assert.Throws<ArgumentNullException>(() => new SqlRootExpression(null, resourceTableExpressions));
        }

        [Fact]
        public void GivenNullResourceTableExpressions_WhenConstructed_ThenThrowsArgumentNullException()
        {
            var searchParamTableExpressions = new List<SearchParamTableExpression>();

            Assert.Throws<ArgumentNullException>(() => new SqlRootExpression(searchParamTableExpressions, null));
        }

        [Fact]
        public void GivenEmptyCollections_WhenConstructed_ThenPropertiesAreEmpty()
        {
            var expression = new SqlRootExpression(
                Array.Empty<SearchParamTableExpression>(),
                Array.Empty<SearchParameterExpressionBase>());

            Assert.Empty(expression.SearchParamTableExpressions);
            Assert.Empty(expression.ResourceTableExpressions);
        }

        [Fact]
        public void GivenSearchParamTableExpressionsArray_WhenWithSearchParamTableExpressions_ThenCreatesExpressionWithEmptyResourceTable()
        {
            var tableExpression = new SearchParamTableExpression(TokenQueryGenerator.Instance, null, SearchParamTableExpressionKind.Normal);

            var expression = SqlRootExpression.WithSearchParamTableExpressions(tableExpression);

            Assert.Single(expression.SearchParamTableExpressions);
            Assert.Same(tableExpression, expression.SearchParamTableExpressions[0]);
            Assert.Empty(expression.ResourceTableExpressions);
        }

        [Fact]
        public void GivenResourceTableExpressionsArray_WhenWithResourceTableExpressions_ThenCreatesExpressionWithEmptySearchParamTable()
        {
            var resourceExpression = new SearchParameterExpression(
                TestSearchParam,
                Expression.Equals(FieldName.TokenCode, null, "active"));

            var expression = SqlRootExpression.WithResourceTableExpressions(resourceExpression);

            Assert.Single(expression.ResourceTableExpressions);
            Assert.Same(resourceExpression, expression.ResourceTableExpressions[0]);
            Assert.Empty(expression.SearchParamTableExpressions);
        }

        [Fact]
        public void GivenSearchParamTableExpressionsList_WhenWithSearchParamTableExpressions_ThenCreatesExpressionWithEmptyResourceTable()
        {
            var tableExpressions = new List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(TokenQueryGenerator.Instance, null, SearchParamTableExpressionKind.Normal),
                new SearchParamTableExpression(TokenQueryGenerator.Instance, null, SearchParamTableExpressionKind.Chain),
            };

            var expression = SqlRootExpression.WithSearchParamTableExpressions(tableExpressions);

            Assert.Equal(2, expression.SearchParamTableExpressions.Count);
            Assert.Same(tableExpressions, expression.SearchParamTableExpressions);
            Assert.Empty(expression.ResourceTableExpressions);
        }

        [Fact]
        public void GivenResourceTableExpressionsList_WhenWithResourceTableExpressions_ThenCreatesExpressionWithEmptySearchParamTable()
        {
            var resourceExpressions = new List<SearchParameterExpressionBase>
            {
                new SearchParameterExpression(TestSearchParam, Expression.Equals(FieldName.TokenCode, null, "active")),
                new MissingSearchParameterExpression(TestSearchParam, isMissing: true),
            };

            var expression = SqlRootExpression.WithResourceTableExpressions(resourceExpressions);

            Assert.Equal(2, expression.ResourceTableExpressions.Count);
            Assert.Same(resourceExpressions, expression.ResourceTableExpressions);
            Assert.Empty(expression.SearchParamTableExpressions);
        }

        [Fact]
        public void GivenEmptyCollections_WhenToString_ThenFormatsWithoutExpressions()
        {
            var expression = new SqlRootExpression(
                Array.Empty<SearchParamTableExpression>(),
                Array.Empty<SearchParameterExpressionBase>());

            var result = expression.ToString();

            Assert.Contains("SqlRoot", result);
            Assert.Contains("SearchParamTables:", result);
            Assert.Contains("ResourceTable:", result);
        }

        [Fact]
        public void GivenSearchParamTableExpressions_WhenToString_ThenIncludesExpressions()
        {
            var tableExpression = new SearchParamTableExpression(TokenQueryGenerator.Instance, null, SearchParamTableExpressionKind.Normal);
            var expression = SqlRootExpression.WithSearchParamTableExpressions(tableExpression);

            var result = expression.ToString();

            Assert.Contains("SqlRoot", result);
            Assert.Contains("SearchParamTables:", result);
            Assert.Contains("Table", result);
        }

        [Fact]
        public void GivenResourceTableExpressions_WhenToString_ThenIncludesExpressions()
        {
            var resourceExpression = new SearchParameterExpression(
                TestSearchParam,
                Expression.Equals(FieldName.TokenCode, null, "active"));
            var expression = SqlRootExpression.WithResourceTableExpressions(resourceExpression);

            var result = expression.ToString();

            Assert.Contains("SqlRoot", result);
            Assert.Contains("ResourceTable:", result);
        }

        [Fact]
        public void GivenSameExpressions_WhenValueInsensitiveEquals_ThenReturnsTrue()
        {
            var tableExpression = new SearchParamTableExpression(TokenQueryGenerator.Instance, null, SearchParamTableExpressionKind.Normal);
            var resourceExpression = new SearchParameterExpression(
                TestSearchParam,
                Expression.Equals(FieldName.TokenCode, null, "active"));

            var expression1 = new SqlRootExpression(
                new[] { tableExpression },
                new[] { resourceExpression });

            var expression2 = new SqlRootExpression(
                new[] { tableExpression },
                new[] { resourceExpression });

            Assert.True(expression1.ValueInsensitiveEquals(expression2));
            Assert.True(expression2.ValueInsensitiveEquals(expression1));
        }

        [Fact]
        public void GivenNull_WhenValueInsensitiveEquals_ThenReturnsFalse()
        {
            var expression = SqlRootExpression.WithSearchParamTableExpressions();

            Assert.False(expression.ValueInsensitiveEquals(null));
        }

        [Fact]
        public void GivenDifferentExpressionType_WhenValueInsensitiveEquals_ThenReturnsFalse()
        {
            var rootExpression = SqlRootExpression.WithSearchParamTableExpressions();
            var otherExpression = Expression.Equals(FieldName.TokenCode, null, "test");

            Assert.False(rootExpression.ValueInsensitiveEquals(otherExpression));
        }

        [Fact]
        public void GivenDifferentSearchParamTableExpressionsCount_WhenValueInsensitiveEquals_ThenReturnsFalse()
        {
            var expression1 = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(TokenQueryGenerator.Instance, null, SearchParamTableExpressionKind.Normal));

            var expression2 = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(TokenQueryGenerator.Instance, null, SearchParamTableExpressionKind.Normal),
                new SearchParamTableExpression(TokenQueryGenerator.Instance, null, SearchParamTableExpressionKind.Chain));

            Assert.False(expression1.ValueInsensitiveEquals(expression2));
        }

        [Fact]
        public void GivenDifferentResourceTableExpressionsCount_WhenValueInsensitiveEquals_ThenReturnsFalse()
        {
            var expression1 = SqlRootExpression.WithResourceTableExpressions(
                new SearchParameterExpression(TestSearchParam, Expression.Equals(FieldName.TokenCode, null, "active")));

            var expression2 = SqlRootExpression.WithResourceTableExpressions(
                new SearchParameterExpression(TestSearchParam, Expression.Equals(FieldName.TokenCode, null, "active")),
                new MissingSearchParameterExpression(TestSearchParam, isMissing: true));

            Assert.False(expression1.ValueInsensitiveEquals(expression2));
        }

        [Fact]
        public void GivenDifferentSearchParamTableExpressions_WhenValueInsensitiveEquals_ThenReturnsFalse()
        {
            var expression1 = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(TokenQueryGenerator.Instance, null, SearchParamTableExpressionKind.Normal));

            var expression2 = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(TokenQueryGenerator.Instance, null, SearchParamTableExpressionKind.Chain));

            Assert.False(expression1.ValueInsensitiveEquals(expression2));
        }

        [Fact]
        public void GivenDifferentResourceTableExpressions_WhenValueInsensitiveEquals_ThenReturnsFalse()
        {
            var expression1 = SqlRootExpression.WithResourceTableExpressions(
                new SearchParameterExpression(TestSearchParam, Expression.Equals(FieldName.TokenCode, null, "active")));

            var expression2 = SqlRootExpression.WithResourceTableExpressions(
                new MissingSearchParameterExpression(TestSearchParam, isMissing: true));

            Assert.False(expression1.ValueInsensitiveEquals(expression2));
        }

        [Fact]
        public void GivenSearchParamTableExpressionsInDifferentOrder_WhenValueInsensitiveEquals_ThenReturnsFalse()
        {
            var tableExpression1 = new SearchParamTableExpression(TokenQueryGenerator.Instance, null, SearchParamTableExpressionKind.Normal);
            var tableExpression2 = new SearchParamTableExpression(TokenQueryGenerator.Instance, null, SearchParamTableExpressionKind.Chain);

            var expression1 = SqlRootExpression.WithSearchParamTableExpressions(tableExpression1, tableExpression2);
            var expression2 = SqlRootExpression.WithSearchParamTableExpressions(tableExpression2, tableExpression1);

            Assert.False(expression1.ValueInsensitiveEquals(expression2));
        }

        [Fact]
        public void GivenResourceTableExpressionsInDifferentOrder_WhenValueInsensitiveEquals_ThenReturnsFalse()
        {
            var resourceExpression1 = new SearchParameterExpression(
                TestSearchParam,
                Expression.Equals(FieldName.TokenCode, null, "active"));

            var resourceExpression2 = new MissingSearchParameterExpression(TestSearchParam, isMissing: true);

            var expression1 = SqlRootExpression.WithResourceTableExpressions(resourceExpression1, resourceExpression2);
            var expression2 = SqlRootExpression.WithResourceTableExpressions(resourceExpression2, resourceExpression1);

            Assert.False(expression1.ValueInsensitiveEquals(expression2));
        }

        [Fact]
        public void GivenEmptyExpressions_WhenValueInsensitiveEquals_ThenReturnsTrue()
        {
            var expression1 = new SqlRootExpression(
                Array.Empty<SearchParamTableExpression>(),
                Array.Empty<SearchParameterExpressionBase>());

            var expression2 = new SqlRootExpression(
                Array.Empty<SearchParamTableExpression>(),
                Array.Empty<SearchParameterExpressionBase>());

            Assert.True(expression1.ValueInsensitiveEquals(expression2));
        }

        [Fact]
        public void GivenSameExpressions_WhenAddValueInsensitiveHashCode_ThenProducesSameHashCode()
        {
            var tableExpression = new SearchParamTableExpression(TokenQueryGenerator.Instance, null, SearchParamTableExpressionKind.Normal);
            var resourceExpression = new SearchParameterExpression(
                TestSearchParam,
                Expression.Equals(FieldName.TokenCode, null, "active"));

            var expression1 = new SqlRootExpression(
                new[] { tableExpression },
                new[] { resourceExpression });

            var expression2 = new SqlRootExpression(
                new[] { tableExpression },
                new[] { resourceExpression });

            var hashCode1 = default(HashCode);
            expression1.AddValueInsensitiveHashCode(ref hashCode1);

            var hashCode2 = default(HashCode);
            expression2.AddValueInsensitiveHashCode(ref hashCode2);

            Assert.Equal(hashCode1.ToHashCode(), hashCode2.ToHashCode());
        }

        [Fact]
        public void GivenDifferentSearchParamTableExpressions_WhenAddValueInsensitiveHashCode_ThenProducesDifferentHashCodes()
        {
            var expression1 = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(TokenQueryGenerator.Instance, null, SearchParamTableExpressionKind.Normal));

            var expression2 = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(StringQueryGenerator.Instance, null, SearchParamTableExpressionKind.Normal));

            var hashCode1 = default(HashCode);
            expression1.AddValueInsensitiveHashCode(ref hashCode1);

            var hashCode2 = default(HashCode);
            expression2.AddValueInsensitiveHashCode(ref hashCode2);

            Assert.NotEqual(hashCode1.ToHashCode(), hashCode2.ToHashCode());
        }

        [Fact]
        public void GivenDifferentResourceTableExpressions_WhenAddValueInsensitiveHashCode_ThenProducesDifferentHashCodes()
        {
            var expression1 = SqlRootExpression.WithResourceTableExpressions(
                new SearchParameterExpression(TestSearchParam, Expression.Equals(FieldName.TokenCode, null, "active")));

            var expression2 = SqlRootExpression.WithResourceTableExpressions(
                new MissingSearchParameterExpression(TestSearchParam, isMissing: true));

            var hashCode1 = default(HashCode);
            expression1.AddValueInsensitiveHashCode(ref hashCode1);

            var hashCode2 = default(HashCode);
            expression2.AddValueInsensitiveHashCode(ref hashCode2);

            Assert.NotEqual(hashCode1.ToHashCode(), hashCode2.ToHashCode());
        }

        [Fact]
        public void GivenEmptyCollections_WhenAddValueInsensitiveHashCode_ThenHandlesGracefully()
        {
            var expression = new SqlRootExpression(
                Array.Empty<SearchParamTableExpression>(),
                Array.Empty<SearchParameterExpressionBase>());

            var hashCode = default(HashCode);

            var exception = Record.Exception(() => expression.AddValueInsensitiveHashCode(ref hashCode));

            Assert.Null(exception);
        }

        [Fact]
        public void GivenMultipleSearchParamTableExpressions_WhenAddValueInsensitiveHashCode_ThenIncludesAllExpressions()
        {
            var expression1 = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(TokenQueryGenerator.Instance, null, SearchParamTableExpressionKind.Normal),
                new SearchParamTableExpression(TokenQueryGenerator.Instance, null, SearchParamTableExpressionKind.Chain));

            var expression2 = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(TokenQueryGenerator.Instance, null, SearchParamTableExpressionKind.Normal));

            var hashCode1 = default(HashCode);
            expression1.AddValueInsensitiveHashCode(ref hashCode1);

            var hashCode2 = default(HashCode);
            expression2.AddValueInsensitiveHashCode(ref hashCode2);

            Assert.NotEqual(hashCode1.ToHashCode(), hashCode2.ToHashCode());
        }

        [Fact]
        public void GivenMultipleResourceTableExpressions_WhenAddValueInsensitiveHashCode_ThenIncludesAllExpressions()
        {
            var expression1 = SqlRootExpression.WithResourceTableExpressions(
                new SearchParameterExpression(TestSearchParam, Expression.Equals(FieldName.TokenCode, null, "active")),
                new MissingSearchParameterExpression(TestSearchParam, isMissing: true));

            var expression2 = SqlRootExpression.WithResourceTableExpressions(
                new SearchParameterExpression(TestSearchParam, Expression.Equals(FieldName.TokenCode, null, "active")));

            var hashCode1 = default(HashCode);
            expression1.AddValueInsensitiveHashCode(ref hashCode1);

            var hashCode2 = default(HashCode);
            expression2.AddValueInsensitiveHashCode(ref hashCode2);

            Assert.NotEqual(hashCode1.ToHashCode(), hashCode2.ToHashCode());
        }

        [Fact]
        public void GivenNullVisitor_WhenAcceptVisitor_ThenThrowsArgumentNullException()
        {
            var expression = SqlRootExpression.WithSearchParamTableExpressions();

            Assert.Throws<ArgumentNullException>(() => expression.AcceptVisitor<object, object>(null, null));
        }
    }
}

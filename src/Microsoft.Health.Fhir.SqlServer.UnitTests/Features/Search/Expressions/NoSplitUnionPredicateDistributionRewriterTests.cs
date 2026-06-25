// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
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
    public class NoSplitUnionPredicateDistributionRewriterTests
    {
        private static readonly SearchParameterExpression ShortBranch = new(
            new SearchParameterInfo("birthdate", "birthdate"),
            Expression.Equals(FieldName.DateTimeEnd, null, new DateTimeOffset(2016, 7, 6, 23, 59, 59, TimeSpan.Zero)));

        private static readonly SearchParameterExpression LongBranch = new(
            new SearchParameterInfo("birthdate", "birthdate"),
            Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, new DateTimeOffset(2016, 7, 6, 0, 0, 0, TimeSpan.Zero)));

        private static readonly SearchParameterExpression TypeRemainder = new(
            new SearchParameterInfo(SearchParameterNames.ResourceType, SearchParameterNames.ResourceType),
            Expression.Equals(FieldName.String, null, "Patient"));

        [Fact]
        public void GivenNoSplitUnionAndedWithRemainder_WhenRewritten_RemainderDistributedIntoEachBranchAndOuterAndRemoved()
        {
            UnionExpression union = Expression.Union(UnionOperator.All, new Expression[] { ShortBranch, LongBranch });
            union.DoNotSplitIntoSeparateCtes = true;

            var input = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(DateTimeQueryGenerator.Instance, Expression.And(union, TypeRemainder), SearchParamTableExpressionKind.Normal));

            var result = (SqlRootExpression)input.AcceptVisitor(NoSplitUnionPredicateDistributionRewriter.Instance);

            var rewrittenUnion = Assert.IsType<UnionExpression>(result.SearchParamTableExpressions[0].Predicate);
            Assert.True(rewrittenUnion.DoNotSplitIntoSeparateCtes);
            Assert.Equal(2, rewrittenUnion.Expressions.Count);
            Assert.Equal(Expression.And(ShortBranch, TypeRemainder).ToString(), rewrittenUnion.Expressions[0].ToString());
            Assert.Equal(Expression.And(LongBranch, TypeRemainder).ToString(), rewrittenUnion.Expressions[1].ToString());
        }

        [Fact]
        public void GivenBareNoSplitUnion_WhenRewritten_ReturnsOriginalExpression()
        {
            UnionExpression union = Expression.Union(UnionOperator.All, new Expression[] { ShortBranch, LongBranch });
            union.DoNotSplitIntoSeparateCtes = true;

            var input = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(DateTimeQueryGenerator.Instance, union, SearchParamTableExpressionKind.Normal));

            var result = (SqlRootExpression)input.AcceptVisitor(NoSplitUnionPredicateDistributionRewriter.Instance);

            Assert.Same(input, result);
        }

        [Fact]
        public void GivenSplitUnionAndedWithRemainder_WhenRewritten_ReturnsOriginalExpression()
        {
            UnionExpression union = Expression.Union(UnionOperator.All, new Expression[] { ShortBranch, LongBranch });
            union.DoNotSplitIntoSeparateCtes = false;

            var input = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(DateTimeQueryGenerator.Instance, Expression.And(union, TypeRemainder), SearchParamTableExpressionKind.Normal));

            var result = (SqlRootExpression)input.AcceptVisitor(NoSplitUnionPredicateDistributionRewriter.Instance);

            Assert.Same(input, result);
        }
    }
}

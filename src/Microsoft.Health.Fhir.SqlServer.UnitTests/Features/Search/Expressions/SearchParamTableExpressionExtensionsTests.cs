// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchParamTableExpressionExtensionsTests
    {
        [Fact]
        public void GivenSearchParamTableExpressionWithTopLevelUnion_WhenSplitExpressions_ThenUnionIsReturned()
        {
            var union = new UnionExpression(
                UnionOperator.All,
                new[]
                {
                    BuildBirthdateBranch(false, FieldName.DateTimeEnd, BinaryOperator.Equal),
                    BuildBirthdateBranch(true, FieldName.DateTimeStart, BinaryOperator.GreaterThanOrEqual),
                });
            var tableExpression = new SearchParamTableExpression(DateTimeQueryGenerator.Instance, union, SearchParamTableExpressionKind.Normal);

            Assert.True(tableExpression.HasUnionAllExpression());
            Assert.Equal(1, new[] { tableExpression }.GetCountOfUnionAllExpressions());

            bool split = tableExpression.SplitExpressions(out UnionExpression splitUnion, out SearchParamTableExpression remainingExpression);

            Assert.True(split);
            Assert.Same(union, splitUnion);
            Assert.Null(remainingExpression);
        }

        private static SearchParameterExpression BuildBirthdateBranch(bool isLongerThanADay, FieldName fieldName, BinaryOperator binaryOperator)
        {
            var birthdateParam = new SearchParameterInfo(
                "birthdate",
                "birthdate",
                SearchParamType.Date,
                new Uri("http://hl7.org/fhir/SearchParameter/individual-birthdate"),
                expression: "Patient.birthDate",
                baseResourceTypes: new[] { "Patient" });

            return new SearchParameterExpression(
                birthdateParam,
                Expression.And(
                    Expression.Equals(SqlFieldName.DateTimeIsLongerThanADay, null, isLongerThanADay),
                    new BinaryExpression(binaryOperator, fieldName, null, new DateTimeOffset(1990, 5, 15, 0, 0, 0, TimeSpan.Zero))));
        }
    }
}

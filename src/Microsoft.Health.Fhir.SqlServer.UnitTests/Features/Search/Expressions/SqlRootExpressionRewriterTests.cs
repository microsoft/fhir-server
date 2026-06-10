// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
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
    public class SqlRootExpressionRewriterTests
    {
        [Fact]
        public void GivenTopLevelUnionExpression_WhenRewritten_ThenTableExpressionKindIsNormal()
        {
            var rewriter = new SqlRootExpressionRewriter(new SearchParamTableExpressionQueryGeneratorFactory(new SearchParameterToSearchValueTypeMap()));
            var union = new UnionExpression(
                UnionOperator.All,
                new[]
                {
                    BuildBirthdateBranch(FieldName.DateTimeEnd, BinaryOperator.Equal),
                    BuildBirthdateBranch(FieldName.DateTimeStart, BinaryOperator.GreaterThanOrEqual),
                });

            var result = (SqlRootExpression)union.AcceptVisitor(rewriter, 0);

            var tableExpression = Assert.Single(result.SearchParamTableExpressions);
            Assert.Equal(SearchParamTableExpressionKind.Normal, tableExpression.Kind);
            Assert.Same(union, tableExpression.Predicate);
        }

        private static SearchParameterExpression BuildBirthdateBranch(FieldName fieldName, BinaryOperator binaryOperator)
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
                new BinaryExpression(binaryOperator, fieldName, null, new DateTimeOffset(1990, 5, 15, 0, 0, 0, TimeSpan.Zero)));
        }
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Expressions
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ExpressionToStringTests
    {
        [Fact]
        public void GivenAnExpression_WhenCallingToString_ReturnsAnUnderstandableString()
        {
            VerifyExpression("(FieldEqual Quantity 1)", Expression.Equals(FieldName.Quantity, null, 1));
            VerifyExpression("(FieldEqual QuantityCode 'a')", Expression.Equals(FieldName.QuantityCode, null, "a"));
            VerifyExpression("(FieldEqual [0].QuantityCode 'a')", Expression.Equals(FieldName.QuantityCode, 0, "a"));

            VerifyExpression("(StringEquals TokenText 'a')", Expression.StringEquals(FieldName.TokenText, null, "a", false));
            VerifyExpression("(StringEqualsIgnoreCase TokenText 'a')", Expression.StringEquals(FieldName.TokenText, null, "a", true));
            VerifyExpression("(StringEqualsIgnoreCase [0].TokenText 'a')", Expression.StringEquals(FieldName.TokenText, 0, "a", true));

            VerifyExpression("(Param my-param (FieldEqual Quantity 'a'))", Expression.SearchParameter(new SearchParameterInfo("my-param", "my-param"), Expression.Equals(FieldName.Quantity, null, "a")));

            VerifyExpression("(MissingParam my-param)", Expression.MissingSearchParameter(new SearchParameterInfo("my-param", "my-param"), true));
            VerifyExpression("(NotMissingParam my-param)", Expression.MissingSearchParameter(new SearchParameterInfo("my-param", "my-param"), false));

            VerifyExpression("(And (FieldGreaterThan Quantity 1) (FieldLessThan Quantity 10))", Expression.And(Expression.GreaterThan(FieldName.Quantity, null, 1), Expression.LessThan(FieldName.Quantity, null, 10)));

            VerifyExpression("(MissingField Quantity)", Expression.Missing(FieldName.Quantity, null));
            VerifyExpression("(MissingField [0].Quantity)", Expression.Missing(FieldName.Quantity, 0));

            VerifyExpression("(Compartment Patient 'x')", Expression.CompartmentSearch("Patient", "x"));

            VerifyExpression("(Chain subject:Patient (FieldGreaterThan DateTimeEnd 2000-01-01T00:00:00.0000000))", Expression.Chained(new[] { "Observation" }, new SearchParameterInfo("subject", "subject"), new[] { "Patient" }, false, Expression.GreaterThan(FieldName.DateTimeEnd, null, new DateTime(2000, 1, 1))));

            VerifyExpression("(Reverse Chain subject:Observation (FieldGreaterThan DateTimeEnd 2000-01-01T00:00:00.0000000))", Expression.Chained(new[] { "Patient" }, new SearchParameterInfo("subject", "subject"), new[] { "Observation" }, true, Expression.GreaterThan(FieldName.DateTimeEnd, null, new DateTime(2000, 1, 1))));
        }

        private static void VerifyExpression(string expected, Expression expression)
        {
            Assert.Equal(expected, expression.ToString());
        }
    }
}

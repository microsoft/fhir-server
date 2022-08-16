// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Expressions
{
    [Trait(Traits.Category, Categories.Search)]
    [Trait(Traits.Category, Categories.CompartmentSearch)]
    public class UnionExpressionTests
    {
        [Fact]
        public void GivenUnionExpression_WhenInitializedProperly_CreateAnInstanceOfUnionExpression()
        {
            StringExpression expression1 = new StringExpression(StringOperator.Equals, FieldName.String, componentIndex: 0, value: "rush", ignoreCase: true);
            StringExpression expression2 = new StringExpression(StringOperator.Equals, FieldName.String, componentIndex: 0, value: "2112", ignoreCase: true);

            UnionExpression unionExpression1 = new UnionExpression(UnionOperator.All, new Expression[] { expression1, expression2 });

            MultiaryExpression multiaryExpression1 = new MultiaryExpression(MultiaryOperator.And, new Expression[] { expression1, expression2 });

            UnionExpression unionExpression2 = new UnionExpression(UnionOperator.All, new Expression[] { multiaryExpression1 });
        }

        [Fact]
        public void GivenUnionExpression_WhenInitializedImproperly_ThrownAnInvalidOperationException()
        {
            StringExpression expression1 = new StringExpression(StringOperator.Equals, FieldName.String, componentIndex: 0, value: "rush", ignoreCase: true);
            StringExpression expression2 = new StringExpression(StringOperator.Equals, FieldName.String, componentIndex: 0, value: "2112", ignoreCase: true);

            UnionExpression unionExpression1 = new UnionExpression(UnionOperator.All, new Expression[] { expression1, expression2 });

            Assert.Throws<InvalidOperationException>(() =>
            {
                UnionExpression unionExpressionFail1 = new UnionExpression(UnionOperator.All, new Expression[] { unionExpression1 });
            });

            Assert.Throws<InvalidOperationException>(() =>
            {
                MultiaryExpression multiaryExpression1 = new MultiaryExpression(MultiaryOperator.And, new Expression[] { unionExpression1 });

                UnionExpression unionExpressionFail2 = new UnionExpression(UnionOperator.All, new Expression[] { multiaryExpression1 });
            });
        }
    }
}

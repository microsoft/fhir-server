// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class RemoveIncludesRewriterTests
    {
        [Fact]
        public void GivenAnExpressionWithIncludes_WhenVisitedByRemoveIncludesRewriter_IncludesAreRemoved()
        {
            IncludeExpression includeExpression = Expression.Include(new[] { "a" }, new SearchParameterInfo("p", "Token"), "Observation", "Patient", null, false, false, false);

            BinaryExpression fieldExpression = Expression.Equals(FieldName.Number, null, 1);

            Assert.Null(includeExpression.AcceptVisitor(RemoveIncludesRewriter.Instance));
            Assert.Null(Expression.And(includeExpression, includeExpression).AcceptVisitor(RemoveIncludesRewriter.Instance));

            Assert.Same(fieldExpression, fieldExpression.AcceptVisitor(RemoveIncludesRewriter.Instance));
            var andWithoutIncludes = Expression.And(fieldExpression, fieldExpression);
            Assert.Same(andWithoutIncludes, andWithoutIncludes.AcceptVisitor(RemoveIncludesRewriter.Instance));

            Assert.Same(fieldExpression, Expression.And(includeExpression, fieldExpression).AcceptVisitor(RemoveIncludesRewriter.Instance));
            Assert.Same(fieldExpression, Expression.And(fieldExpression, includeExpression).AcceptVisitor(RemoveIncludesRewriter.Instance));

            Assert.Equal(andWithoutIncludes.ToString(), Expression.And(includeExpression, fieldExpression, fieldExpression).AcceptVisitor(RemoveIncludesRewriter.Instance).ToString());
            Assert.Equal(andWithoutIncludes.ToString(), Expression.And(fieldExpression, includeExpression, fieldExpression).AcceptVisitor(RemoveIncludesRewriter.Instance).ToString());
            Assert.Equal(andWithoutIncludes.ToString(), Expression.And(fieldExpression, fieldExpression, includeExpression).AcceptVisitor(RemoveIncludesRewriter.Instance).ToString());
        }
    }
}

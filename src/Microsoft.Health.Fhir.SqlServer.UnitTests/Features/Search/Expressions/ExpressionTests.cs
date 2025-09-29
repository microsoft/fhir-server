// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    public sealed class ExpressionTests
    {
        internal static void ValidateUniqueExpressionIdentifier(Expression expression)
        {
            var identifier = expression.GetUniqueExpressionIdentifier();
            var hash = expression.GetHashedUniqueExpressionIdentifier();
            Assert.NotEqual(identifier, hash);
            Assert.Equal(64, hash.Length);
        }
    }
}

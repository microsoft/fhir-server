// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents the binary operator.
    /// Note that we rely on the relative order of the values in DateTimeBoundedRangeRewriter.
    /// </summary>
    public enum BinaryOperator
    {
        Equal = 0,
        GreaterThan = 1,
        GreaterThanOrEqual = 2,
        LessThan = 3,
        LessThanOrEqual = 4,
        NotEqual = 5,
    }
}

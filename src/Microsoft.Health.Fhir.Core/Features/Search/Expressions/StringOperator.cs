// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents a string operator.
    /// </summary>
    public enum StringOperator
    {
        Contains,
        EndsWith,
        Equals,
        NotContains,
        NotEndsWith,
        NotStartsWith,
        StartsWith,
    }
}

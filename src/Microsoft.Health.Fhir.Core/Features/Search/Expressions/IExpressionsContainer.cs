// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Interface contract for all expression types acting like containers for multiple expressions.
    /// </summary>
    public interface IExpressionsContainer
    {
        IReadOnlyList<Expression> Expressions { get; }
    }
}

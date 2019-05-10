// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.SqlServer.Features.Schema.Model
{
    /// <summary>
    /// Generates a sequence of row structs for a table-valued parameter.
    /// </summary>
    /// <typeparam name="TInput">The input type</typeparam>
    /// <typeparam name="TRow">The row struct type</typeparam>
    internal interface ITableValuedParameterRowGenerator<in TInput,  out TRow>
        where TRow : struct
    {
        IEnumerable<TRow> GenerateRows(TInput input);
    }
}

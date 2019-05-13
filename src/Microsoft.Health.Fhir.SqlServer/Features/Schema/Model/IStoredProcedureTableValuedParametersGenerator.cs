// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Schema.Model
{
    /// <summary>
    /// Generates the full set of table-valued parameters for a stored procedure.
    /// </summary>
    /// <typeparam name="TInput">The type of the input</typeparam>
    /// <typeparam name="TOutput">The type of the output. Intended to be a struct with properties for each TVP</typeparam>
    internal interface IStoredProcedureTableValuedParametersGenerator<in TInput, out TOutput>
    {
        TOutput Generate(TInput input);
    }
}

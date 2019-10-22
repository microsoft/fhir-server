// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// Enum for the _total parameter values.
    /// </summary>
    public enum TotalType
    {
        // There is no need to populate the total count; the client will not use it.
        None = 0,

        // The client requests that the server provide an exact total of the number of matching resources.
        Accurate = 1,

        // A rough estimate of the number of matching resources is sufficient.
        Estimate = 2,
    }
}

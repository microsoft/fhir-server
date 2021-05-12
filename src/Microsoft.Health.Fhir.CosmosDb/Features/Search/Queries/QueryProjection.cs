// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.CosmosDb.Features.Search.Queries
{
    internal enum QueryProjection
    {
        /// <summary>
        /// Default columns selected
        /// </summary>
        Default,

        /// <summary>
        /// Only the ID and Type columns are selected (can be ID + Reference IDs)
        /// </summary>
        IdAndType,

        /// <summary>
        /// Only reference IDs are selected
        /// </summary>
        ReferencesOnly,
    }
}

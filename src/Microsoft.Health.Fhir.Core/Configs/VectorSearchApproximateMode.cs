// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    /// <summary>
    /// Specifies whether semantic queries may use approximate nearest-neighbor retrieval.
    /// </summary>
    public enum VectorSearchApproximateMode
    {
        /// <summary>
        /// Always ranks exactly. This is the default.
        /// </summary>
        Disabled = 0,

        /// <summary>
        /// Uses approximate retrieval when the database has a compatible vector index; otherwise ranks exactly.
        /// </summary>
        Auto = 1,
    }
}

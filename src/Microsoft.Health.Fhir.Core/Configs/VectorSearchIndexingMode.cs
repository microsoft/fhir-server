// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    /// <summary>
    /// Specifies when embeddings are generated for indexed resources.
    /// </summary>
    public enum VectorSearchIndexingMode
    {
        /// <summary>
        /// Generates embeddings in the resource write path.
        /// </summary>
        Synchronous,
    }
}

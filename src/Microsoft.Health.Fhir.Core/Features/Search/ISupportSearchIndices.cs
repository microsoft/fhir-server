// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// Provides search indices.
    /// </summary>
    public interface ISupportSearchIndices
    {
        /// <summary>
        /// Gets search indices.
        /// </summary>
        IReadOnlyCollection<SearchIndexEntry> SearchIndices { get; }
    }
}

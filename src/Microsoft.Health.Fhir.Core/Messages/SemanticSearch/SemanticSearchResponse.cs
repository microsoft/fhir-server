// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.SemanticSearch
{
    /// <summary>
    /// Contains the FHIR search Bundle produced by semantic search.
    /// </summary>
    public sealed class SemanticSearchResponse
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SemanticSearchResponse"/> class.
        /// </summary>
        public SemanticSearchResponse(ResourceElement bundle)
        {
            Bundle = EnsureArg.IsNotNull(bundle, nameof(bundle));
        }

        /// <summary>Gets the result Bundle.</summary>
        public ResourceElement Bundle { get; }
    }
}

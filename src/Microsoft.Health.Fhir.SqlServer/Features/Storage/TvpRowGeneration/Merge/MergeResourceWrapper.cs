// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration.Merge
{
    internal class MergeResourceWrapper
    {
        internal MergeResourceWrapper(ResourceWrapper resourceWrapper, bool keepHistory, bool hasVersionToCompare)
        {
            ResourceWrapper = resourceWrapper;
            KeepHistory = keepHistory;
            HasVersionToCompare = hasVersionToCompare;
        }

        /// <summary>
        /// Resource wrapper
        /// </summary>
        public ResourceWrapper ResourceWrapper { get; private set; }

        /// <summary>
        /// Flag indicating whether resource history is meant to be kept
        /// </summary>
        public bool KeepHistory { get; private set; }

        /// <summary>
        /// Flag indicating whether version in resource wrapper == (existing version in the database + 1)
        /// </summary>
        public bool HasVersionToCompare { get; private set; }
    }
}

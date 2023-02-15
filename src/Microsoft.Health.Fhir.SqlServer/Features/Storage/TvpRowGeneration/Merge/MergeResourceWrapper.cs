// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    internal class MergeResourceWrapper
    {
        internal MergeResourceWrapper(ResourceWrapper resourceWrapper, long resourceSurrogateId, bool keepHistory, bool hasVersionToCompare)
        {
            ResourceWrapper = resourceWrapper;
            ResourceSurrogateId = resourceSurrogateId;
            KeepHistory = keepHistory;
            HasVersionToCompare = hasVersionToCompare;
        }

        /// <summary>
        /// Resource wrapper
        /// </summary>
        public ResourceWrapper ResourceWrapper { get; private set; }

        /// <summary>
        /// Resource surrogate Id
        /// </summary>
        public long ResourceSurrogateId { get; private set; }

        /// <summary>
        /// Flag indicating whether resource history is meant to be kept
        /// </summary>
        public bool KeepHistory { get; private set; }

        /// <summary>
        /// Flag indicating whether version in resource wrapper == (existing version in the database + 1)
        /// </summary>
        public bool HasVersionToCompare { get; private set; }

        /// <summary>
        /// Transaction Id resource is part of
        /// </summary>
        public long? TransactionId { get; internal set; }

        /// <summary>
        /// Reasource record offset in adls file
        /// </summary>
        public int? OffsetInFile { get; internal set; }
    }
}

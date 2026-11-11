// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    /// <summary>
    /// Holds validated semantic pagination keys decoded from the existing continuation-token format.
    /// </summary>
    internal sealed class SemanticSearchContinuationToken
    {
        private SemanticSearchContinuationToken(double distance, short resourceTypeId, long resourceSurrogateId)
        {
            Distance = distance;
            ResourceTypeId = resourceTypeId;
            ResourceSurrogateId = resourceSurrogateId;
        }

        /// <summary>
        /// Gets the finite distance used as the primary pagination key.
        /// </summary>
        public double Distance { get; }

        /// <summary>
        /// Gets the nonzero resource type used as the first tie-breaker.
        /// </summary>
        public short ResourceTypeId { get; }

        /// <summary>
        /// Gets the resource surrogate ID used as the final tie-breaker.
        /// </summary>
        public long ResourceSurrogateId { get; }

        /// <summary>
        /// Creates a token using the existing semantic pagination value constraints.
        /// </summary>
        /// <returns>True if the values are valid; otherwise false with a null token.</returns>
        internal static bool TryCreate(
            double distance,
            short resourceTypeId,
            long resourceSurrogateId,
            out SemanticSearchContinuationToken continuationToken)
        {
            continuationToken = null;
            if (!double.IsFinite(distance) || resourceTypeId == 0)
            {
                return false;
            }

            continuationToken = new SemanticSearchContinuationToken(distance, resourceTypeId, resourceSurrogateId);
            return true;
        }
    }
}

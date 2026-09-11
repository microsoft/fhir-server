// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Search
{
    /// <summary>
    /// A request to search current soft-deleted resources.
    /// </summary>
    public class SearchDeletedResourcesRequest : IRequest<SearchResourceHistoryResponse>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SearchDeletedResourcesRequest"/> class.
        /// </summary>
        public SearchDeletedResourcesRequest(
            string resourceType,
            PartialDateTime since,
            PartialDateTime before,
            int? count,
            string continuationToken,
            string sort)
        {
            ResourceType = resourceType;
            Since = since;
            Before = before;
            Count = count;
            ContinuationToken = continuationToken;
            Sort = sort;
        }

        /// <summary>
        /// Gets the optional resource type.
        /// </summary>
        public string ResourceType { get; }

        /// <summary>
        /// Gets the inclusive lower last-updated bound.
        /// </summary>
        public PartialDateTime Since { get; }

        /// <summary>
        /// Gets the exclusive upper last-updated bound.
        /// </summary>
        public PartialDateTime Before { get; }

        /// <summary>
        /// Gets the requested page size.
        /// </summary>
        public int? Count { get; }

        /// <summary>
        /// Gets the continuation token.
        /// </summary>
        public string ContinuationToken { get; }

        /// <summary>
        /// Gets the last-updated sort order.
        /// </summary>
        public string Sort { get; }
    }
}

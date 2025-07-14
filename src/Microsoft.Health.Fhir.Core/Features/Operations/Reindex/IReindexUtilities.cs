﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Search;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    public interface IReindexUtilities
    {
        /// <summary>
        /// For each result in a batch of resources this will extract new search params
        /// Then compare those to the old values to determine if an update is needed
        /// Needed updates will be committed in a batch
        /// </summary>
        /// <param name="results">The resource batch to process</param>
        /// <param name="batchSize">The number of resources to reindex at a time (e.g. 1000)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A Task</returns>
        Task ProcessSearchResultsAsync(SearchResult results, int batchSize, CancellationToken cancellationToken);
    }
}

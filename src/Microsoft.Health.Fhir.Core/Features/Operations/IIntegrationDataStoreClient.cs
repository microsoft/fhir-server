// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    /// <summary>
    /// Client for integration data store.
    /// </summary>
    public interface IIntegrationDataStoreClient
    {
        /// <summary>
        /// Download resource stream by location
        /// </summary>
        /// <param name="resourceUri">Resource URI</param>
        /// <param name="startOffset">Start offset in the file</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        public Stream DownloadResource(Uri resourceUri, long startOffset, CancellationToken cancellationToken);

        /// <summary>
        /// Prepare for new resource
        /// </summary>
        /// <param name="containerId">Container id for new resourc file</param>
        /// <param name="fileName">Resource file name.</param>
        /// <param name="cancellationToken">Cancellation Token.</param>
        public Task<Uri> PrepareResourceAsync(string containerId, string fileName, CancellationToken cancellationToken);

        /// <summary>
        /// Upload part of resource file in block.
        /// </summary>
        /// <param name="resourceUri">Resource URI.</param>
        /// <param name="stream">Content stream.</param>
        /// <param name="blockId">Id for this block.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public Task UploadBlockAsync(Uri resourceUri, Stream stream, string blockId, CancellationToken cancellationToken);

        /// <summary>
        /// Append new blocks to current resource file.
        /// </summary>
        /// <param name="resourceUri">Resource URI.</param>
        /// <param name="blockIds">New block ids.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public Task AppendCommitAsync(Uri resourceUri, string[] blockIds, CancellationToken cancellationToken);

        /// <summary>
        /// Commit all blocks in resourc file.
        /// </summary>
        /// <param name="resourceUri">Resource URI</param>
        /// <param name="blockIds">Block id list.</param>
        /// <param name="cancellationToken">Cancellation Token.</param>
        public Task CommitAsync(Uri resourceUri, string[] blockIds, CancellationToken cancellationToken);

        /// <summary>
        /// Get resource file properties.
        /// </summary>
        /// <param name="resourceUri">Resource URI.</param>
        /// <param name="cancellationToken">Cancellation Token.</param>
        public Task<Dictionary<string, object>> GetPropertiesAsync(Uri resourceUri, CancellationToken cancellationToken);

        /// <summary>
        /// Try acquire lease on resource file.
        /// </summary>
        /// <param name="resourceUri">Resource URI.</param>
        /// <param name="proposedLeaseId">Proposed LeaseId.</param>
        /// <param name="cancellationToken">Cancellation Token.</param>
        public Task<string> TryAcquireLeaseAsync(Uri resourceUri, string proposedLeaseId, CancellationToken cancellationToken);

        /// <summary>
        /// Try to release lease on resource file.
        /// </summary>
        /// <param name="resourceUri">Resource URI.</param>
        /// <param name="leaseId">Lease id for the resource file.</param>
        /// <param name="cancellationToken">Cancellation Token.</param>
        public Task TryReleaseLeaseAsync(Uri resourceUri, string leaseId, CancellationToken cancellationToken);
    }
}

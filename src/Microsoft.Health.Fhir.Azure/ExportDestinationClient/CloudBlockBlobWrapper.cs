// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using EnsureThat;

namespace Microsoft.Health.Fhir.Azure.ExportDestinationClient
{
    /// <summary>
    /// A wrapper class around <see cref="CloudBlockBlob"/> that also keeps track of the list of existing
    /// block ids in the current blob. Every time we want to update the blob data, we need to commit the list
    /// of new as well as existing block ids we want to keep. Hence the need for tracking block ids instead of
    /// getting it everytime from the blob itself.
    /// </summary>
    public class CloudBlockBlobWrapper
    {
        private readonly OrderedSetOfBlockIds _existingBlockIds;
        private readonly BlobHttpHeaders _blobHeaders;
        private BlockBlobClient _cloudBlob;

        public CloudBlockBlobWrapper(BlockBlobClient blockBlob)
            : this(blockBlob, new List<string>())
        {
        }

        public CloudBlockBlobWrapper(BlockBlobClient blockBlob, IEnumerable<string> blockList)
        {
            EnsureArg.IsNotNull(blockBlob, nameof(blockBlob));
            EnsureArg.IsNotNull(blockList, nameof(blockList));

            _cloudBlob = blockBlob;
            _existingBlockIds = new OrderedSetOfBlockIds(blockList);
            _blobHeaders = new BlobHttpHeaders
            {
                ContentType = "application/fhir+ndjson",
                ContentDisposition = $"attachment; filename=\"{Path.GetFileName(blockBlob.Name)}\"",
                CacheControl = "private, no-cache, no-store, must-revalidate",
            };
        }

        public async Task UploadBlockAsync(string blockId, Stream data, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(blockId, nameof(blockId));
            EnsureArg.IsNotNull(data, nameof(data));

            _existingBlockIds.Add(blockId);
            await _cloudBlob.StageBlockAsync(blockId, data, cancellationToken: cancellationToken);
        }

        public async Task CommitBlockListAsync(CancellationToken cancellationToken)
        {
            await _cloudBlob.CommitBlockListAsync(_existingBlockIds.ToList(), httpHeaders: _blobHeaders, cancellationToken: cancellationToken);
        }

        public void UpdateCloudBlockBlob(BlockBlobClient cloudBlockBlob)
        {
            EnsureArg.IsNotNull(cloudBlockBlob, nameof(cloudBlockBlob));
            EnsureArg.Is(Uri.Compare(_cloudBlob.Uri, cloudBlockBlob.Uri, UriComponents.AbsoluteUri, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase), 0);

            _cloudBlob = cloudBlockBlob;
        }
    }
}

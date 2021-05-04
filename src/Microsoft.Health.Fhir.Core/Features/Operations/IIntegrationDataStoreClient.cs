// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    public interface IIntegrationDataStoreClient
    {
        public Stream DownloadResource(Uri blobUri, long startPosition, CancellationToken cancellationToken);

        public Task<Uri> PrepareResourceAsync(string containerId, string fileName, CancellationToken cancellationToken);

        public Task UploadBlockAsync(Uri resourceUri, Stream stream, string blockId, CancellationToken cancellationToken);

        public Task AppendCommitAsync(Uri resourceUri, string[] blockIds, CancellationToken cancellationToken);

        public Task CommitAsync(Uri resourceUri, string[] blockIds, CancellationToken cancellationToken);

        public Task<T> GetBlockPropertyAsync<T>(string blobUri, string propertyName, CancellationToken cancellationToken);
    }
}

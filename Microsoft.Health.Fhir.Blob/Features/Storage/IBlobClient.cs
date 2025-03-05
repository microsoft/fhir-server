// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Azure.Storage.Blobs;

namespace Microsoft.Health.Fhir.Blob.Features.Storage
{
    /// <summary>
    /// Responsible to get the right BlobContainerClient based on the configuration
    /// </summary>
    public interface IBlobClient
    {
        BlobContainerClient BlobContainerClient { get; }
    }
}

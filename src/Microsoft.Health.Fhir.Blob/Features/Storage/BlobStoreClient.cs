// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Azure.Storage.Blobs;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Blob.Configs;
using Microsoft.Health.Fhir.Blob.Features.Common;

namespace Microsoft.Health.Fhir.Blob.Features.Storage;

/// <summary>
/// Represents the blob container created by the service and initialized during app startup
/// </summary>
public class BlobStoreClient : IBlobClient
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly string _containerName;
    private readonly ILogger _logger;

    public BlobStoreClient(
        BlobServiceClient blobServiceClient,
        IOptionsMonitor<BlobContainerConfiguration> optionsMonitor,
        ILogger<BlobStoreClient> logger)
    {
        _blobServiceClient = EnsureArg.IsNotNull(blobServiceClient, nameof(blobServiceClient));
        _containerName = BlobConstants.BlobRawResourceContainerName;
        _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        _logger.LogInformation("Blob store client registered.");
    }

    public BlobContainerClient BlobContainerClient => _blobServiceClient.GetBlobContainerClient(_containerName);
}

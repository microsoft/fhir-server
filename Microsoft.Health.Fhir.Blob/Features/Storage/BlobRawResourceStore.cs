// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Blob.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Blob.Features.Storage;

/// <summary>
/// This class is responsible for performing blob storage operations for reading/writing raw FHIR resources.
/// </summary>
public class BlobRawResourceStore : IRawResourceStore
{
    private readonly BlobOperationOptions _options;
    private readonly ILogger<BlobRawResourceStore> _logger;
    private readonly IBlobClient _blobClient;
    private static readonly int EndOfLine = Encoding.UTF8.GetByteCount(Environment.NewLine);

    public BlobRawResourceStore(
        IBlobClient blobClient,
        ILogger<BlobRawResourceStore> logger,
        IOptions<BlobOperationOptions> options)
    {
        // TODO: Add metrics for blob operations
        _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        _blobClient = EnsureArg.IsNotNull(blobClient, nameof(blobClient));
        _options = EnsureArg.IsNotNull(options?.Value, nameof(options));
    }

    public async Task<IReadOnlyList<ResourceWrapper>> WriteRawResourcesAsync(IReadOnlyList<ResourceWrapper> rawResources, long storageIdentifier, CancellationToken cancellationToken = default)
    {
        EnsureArg.IsNotNull(rawResources, nameof(rawResources));
        EnsureArg.IsGte(0, storageIdentifier, nameof(storageIdentifier));

        _logger.LogInformation($"Writing raw resources to blob storage for storage identifier: {storageIdentifier}.");

        // prepare the file to store
        int offset = 0;
        using MemoryStream stream = new MemoryStream();
        using StreamWriter writer = new StreamWriter(stream);

        foreach (var resource in rawResources)
        {
            resource.ResourceStorageIdentifier = storageIdentifier;
            resource.ResourceStorageOffset = offset;
            var line = resource.RawResource.Data;
            offset += Encoding.UTF8.GetByteCount(line) + EndOfLine;
            await writer.WriteLineAsync(line);
        }

        BlockBlobClient blobClient = GetNewInstanceBlockBlobClient(storageIdentifier);
        var blobUploadOptions = new BlobUploadOptions { TransferOptions = _options.Upload };

        // TODO: Error handling

        // upload the file to blob storage
        stream.Seek(0, SeekOrigin.Begin);
        await blobClient.UploadAsync(stream, blobUploadOptions, cancellationToken);

        return rawResources;
    }

    protected virtual BlockBlobClient GetNewInstanceBlockBlobClient(long storageIdentifier)
    {
        string blobName = GetBlobName(storageIdentifier);
        return _blobClient.BlobContainerClient.GetBlockBlobClient(blobName);
    }

    private static string GetBlobName(long storageIdentifier)
    {
        return $"{BlobUtility.ComputeHashPrefixForBlobName(storageIdentifier)}/{storageIdentifier}.ndjson";
    }

    private async Task<T> ExecuteAsync<T>(
        Func<Task<T>> func,
        string operationName)
    {
        try
        {
            T result = await func();
            return result;
        }
        catch (RequestFailedException ex) when (ex.ErrorCode == BlobErrorCode.BlobNotFound)
        {
            _logger.LogError(ex, message: "Access to storage account failed with ErrorCode: {ErrorCode}", ex.ErrorCode);
            throw new RawResourceStoreException($"Access to storage account failed with ErrorCode: {ex.ErrorCode}");
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, message: "Access to storage account failed with ErrorCode: {ErrorCode}", ex.ErrorCode);
            throw new RawResourceStoreException($"Access to storage account failed with ErrorCode: {ex.ErrorCode}");
        }
        catch (AggregateException ex) when (ex.InnerException is RequestFailedException)
        {
            var innerEx = ex.InnerException as RequestFailedException;
            _logger.LogError(innerEx, message: "Access to external storage account failed with ErrorCode: {ErrorCode}", innerEx.ErrorCode);
            throw new RawResourceStoreException($"Access to external storage account failed with ErrorCode: {innerEx.ErrorCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Access to storage account failed");
            throw new RawResourceStoreException("Access to storage account failed");
        }
    }
}

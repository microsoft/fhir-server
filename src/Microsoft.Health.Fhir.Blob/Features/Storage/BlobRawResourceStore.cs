// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
using Microsoft.Health.Fhir.Blob.Features.Common;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.IO;

namespace Microsoft.Health.Fhir.Blob.Features.Storage;

/// <summary>
/// This class is responsible for performing blob storage operations for reading/writing raw FHIR resources.
/// </summary>
public class BlobRawResourceStore : IRawResourceStore
{
    private readonly BlobOperationOptions _options;
    private readonly ILogger<BlobRawResourceStore> _logger;
    private readonly IBlobClient _blobClient;
    private readonly RecyclableMemoryStreamManager _recyclableMemoryStreamManager;
    internal static readonly int EndOfLine = Encoding.UTF8.GetByteCount(Environment.NewLine);

    public BlobRawResourceStore(
        IBlobClient blobClient,
        ILogger<BlobRawResourceStore> logger,
        IOptions<BlobOperationOptions> options,
        RecyclableMemoryStreamManager recyclableMemoryStreamManager)
    {
        _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        _blobClient = EnsureArg.IsNotNull(blobClient, nameof(blobClient));
        _options = EnsureArg.IsNotNull(options?.Value, nameof(options));
        _recyclableMemoryStreamManager = EnsureArg.IsNotNull(recyclableMemoryStreamManager, nameof(recyclableMemoryStreamManager));
    }

    public async Task<IReadOnlyList<ResourceWrapper>> WriteRawResourcesAsync(IReadOnlyList<ResourceWrapper> rawResources, long storageIdentifier, CancellationToken cancellationToken = default)
    {
        EnsureArg.IsNotNull(rawResources, nameof(rawResources));
        EnsureArg.IsGte(storageIdentifier, 0, nameof(storageIdentifier));

        Stopwatch timer = Stopwatch.StartNew();
        _logger.LogInformation($"Writing raw resources to blob storage for storage identifier: {storageIdentifier}.");

        // prepare the file to store
        int offset = 0;
        using RecyclableMemoryStream stream = _recyclableMemoryStreamManager.GetStream();
        using StreamWriter writer = new StreamWriter(stream, Encoding.UTF8);
        foreach (var resource in rawResources)
        {
            var line = resource.RawResource.Data;

            // Although RawResource ctor does not allow null or empty data, we are checking here to be safe.
            if (string.IsNullOrEmpty(line))
            {
                resource.ResourceStorageIdentifier = storageIdentifier;
                resource.ResourceStorageOffset = -1; // -1 indicates that the resource is not stored in blob storage.
                resource.ResourceLength = -1;
                continue;
            }

            resource.ResourceStorageIdentifier = storageIdentifier;
            resource.ResourceStorageOffset = offset;
            resource.ResourceLength = Encoding.UTF8.GetByteCount(line);

            offset += resource.ResourceLength + EndOfLine;
            await writer.WriteLineAsync(line);
        }

        await writer.FlushAsync(cancellationToken);
        await stream.FlushAsync(cancellationToken);
        BlockBlobClient blobClient = GetNewInstanceBlockBlobClient(storageIdentifier);
        var blobUploadOptions = new BlobUploadOptions { TransferOptions = _options.Upload };

        // upload the file to blob storage
        stream.Seek(0, SeekOrigin.Begin);

        _ = await ExecuteAsync(
            func: async () =>
            {
                return await blobClient.UploadAsync(stream, blobUploadOptions, cancellationToken);
            },
            operationName: nameof(WriteRawResourcesAsync));

        timer.Stop();
        _logger.LogInformation($"Successfully wrote raw resources to blob storage for storage identifier: {blobClient.Name} in {timer.ElapsedMilliseconds} ms");

        return rawResources;
    }

    public async Task<Dictionary<RawResourceLocator, string>> ReadRawResourcesAsync(IReadOnlyList<RawResourceLocator> rawResourceLocators, CancellationToken cancellationToken)
    {
        EnsureArg.IsNotNull(rawResourceLocators, nameof(rawResourceLocators));
        var completeRawResources = new Dictionary<RawResourceLocator, string>();

        Stopwatch timer = Stopwatch.StartNew();
        _logger.LogInformation($"Reading {rawResourceLocators.Count} raw resources from blob storage");

        var tasks = rawResourceLocators.Select(async resourceLocator =>
        {
            BlockBlobClient blobClient = GetNewInstanceBlockBlobClient(resourceLocator.RawResourceStorageIdentifier);
            var options = new BlobDownloadOptions() { Range = new HttpRange(resourceLocator.RawResourceOffset, resourceLocator.RawResourceLength) };

            Response<BlobDownloadResult> blobResult = await ExecuteAsync(
                func: async () => await blobClient.DownloadContentAsync(options, cancellationToken),
                operationName: nameof(ReadRawResourcesAsync));

            var line = blobResult.Value.Content.ToString();
            lock (completeRawResources)
            {
                completeRawResources.Add(resourceLocator, line);
            }
        }).ToList();

        await Task.WhenAll(tasks);

        timer.Stop();
        _logger.LogInformation($"Successfully read {rawResourceLocators.Count} raw resources from blob storage in {timer.ElapsedMilliseconds} ms");

        return completeRawResources;
    }

    protected BlockBlobClient GetNewInstanceBlockBlobClient(long storageIdentifier)
    {
        string blobName = GetBlobName(storageIdentifier);
        return _blobClient.BlobContainerClient.GetBlockBlobClient(blobName);
    }

    public static string GetBlobName(long storageIdentifier)
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
        catch (RequestFailedException ex)
        {
            var message = string.Format(Resources.RawResourceStoreOperationFailedWithError, ex.ErrorCode);
            _logger.LogError(ex, message: message);
            throw new RawResourceStoreException(message, ex);
        }
        catch (AggregateException ex) when (ex.InnerException is RequestFailedException && ex.InnerException is not null)
        {
            var innerEx = ex.InnerException as RequestFailedException;
            var message = string.Format(Resources.RawResourceStoreOperationFailedWithError, innerEx.ErrorCode);
            _logger.LogError(innerEx, message: message);
            throw new RawResourceStoreException(message, ex);
        }
        catch (Exception ex)
        {
            var message = Resources.RawResourceStoreOperationFailed;
            _logger.LogError(ex, message);
            throw new RawResourceStoreException(message, ex);
        }
    }
}

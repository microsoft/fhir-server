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
        using StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)); // Explicitly set UTF-8 encoding without BOM);

        foreach (var resource in rawResources)
        {
            var line = resource.RawResource.Data;

            // Although RawResource ctor does not allow null or empty data, we are checking here to be safe.
            if (string.IsNullOrEmpty(line))
            {
                resource.ResourceStorageIdentifier = storageIdentifier;
                resource.ResourceStorageOffset = -1; // -1 indicates that the resource is not stored in blob storage.
                continue;
            }

            resource.ResourceStorageIdentifier = storageIdentifier;
            resource.ResourceStorageOffset = offset;

            offset += Encoding.UTF8.GetByteCount(line) + EndOfLine;
            await writer.WriteLineAsync(line);
        }

        // Validate .net version
#if NET8_0_OR_GREATER
        await writer.FlushAsync(cancellationToken);
#else
        await writer.FlushAsync();
#endif

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

    public async Task<Dictionary<RawResourceLocator, string>> ReadRawResourcesAsync(IReadOnlyList<Core.Features.Persistence.RawResourceLocator> rawResourceLocators, CancellationToken cancellationToken)
    {
        EnsureArg.IsNotNull(rawResourceLocators, nameof(rawResourceLocators));
        var completeRawResources = new Dictionary<RawResourceLocator, string>();

        Stopwatch timer = Stopwatch.StartNew();
        _logger.LogInformation($"Reading {rawResourceLocators.Count} raw resources from blob storage");

        var blobOffsetDictionary = GetBlobOffsetCombinations(rawResourceLocators);

        var tasks = blobOffsetDictionary.Select(async storageIdentifier =>
        {
            BlockBlobClient blobClient = GetNewInstanceBlockBlobClient(storageIdentifier.Key);
            var blobReadOptions = new BlobOpenReadOptions(false);

            await ExecuteAsync(
                async () =>
            {
                using (Stream blobStream = await blobClient.OpenReadAsync(blobReadOptions, cancellationToken))
                {
                    using (StreamReader reader = new StreamReader(blobStream, Encoding.UTF8))
                    {
                        foreach (var offset in storageIdentifier.Value.OrderBy(o => o))
                        {
                            blobStream.Seek(offset, SeekOrigin.Begin);
                            reader.DiscardBufferedData(); // Clear StreamReader's buffer to align with the new stream position

#if NET8_0_OR_GREATER
                            string resource = await reader.ReadLineAsync(cancellationToken);
#else
                            string resource = await reader.ReadLineAsync();
#endif

                            if (string.IsNullOrEmpty(resource))
                            {
                                throw new RawResourceStoreException($"Failed to read resource from blob storage for storage identifier: {storageIdentifier.Key} with offset {offset}");
                            }

                            lock (completeRawResources)
                            {
                                completeRawResources.Add(
                                    new RawResourceLocator(storageIdentifier.Key, offset),
                                    resource);
                            }
                        }
                    }
                }

                return Task.CompletedTask;
            },
                nameof(ReadRawResourcesAsync));
        });

        await Task.WhenAll(tasks);

        timer.Stop();
        _logger.LogInformation($"Successfully read {rawResourceLocators.Count} raw resources from blob storage in {timer.ElapsedMilliseconds} ms");

        return completeRawResources;
    }

    internal static Dictionary<long, List<int>> GetBlobOffsetCombinations(IReadOnlyList<RawResourceLocator> rawResourcesLocators)
    {
        // Dictionary containing different blobContainers that need to be read and the list of offsets within the blob
        var fileStorageDictionary = new Dictionary<long, List<int>>();

        foreach (var resourceLocator in rawResourcesLocators)
        {
            if (resourceLocator.RawResourceStorageIdentifier == -1 || resourceLocator.RawResourceOffset < 0)
            {
                continue;
            }

            if (!fileStorageDictionary.TryGetValue(resourceLocator.RawResourceStorageIdentifier, out List<int> value))
            {
                value = new List<int>();
                fileStorageDictionary[resourceLocator.RawResourceStorageIdentifier] = value;
            }

            value.Add(resourceLocator.RawResourceOffset);
        }

        return fileStorageDictionary;
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

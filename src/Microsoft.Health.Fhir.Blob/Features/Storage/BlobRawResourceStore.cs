// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Io;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Blob.Configs;
using Microsoft.Health.Fhir.Blob.Features.Common;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.IO;
using SharpCompress.Compressors.Xz;

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

    // Read a resource form a blob storage, using offset to skip other documents
    public async Task<RawResource> ReadRawResourceAsync(long storageIdentifier, long offset, CancellationToken cancellationToken = default)
    {
        EnsureArg.IsGte(storageIdentifier, 0, nameof(storageIdentifier));
        EnsureArg.IsGte(offset, 0, nameof(offset));

        Stopwatch timer = Stopwatch.StartNew();
        _logger.LogInformation($"Reading raw resource from blob storage for storage identifier: {storageIdentifier} with offset {offset}");

        BlockBlobClient blobClient = GetNewInstanceBlockBlobClient(storageIdentifier);
        var blobDownloadOptions = new BlobDownloadOptions { Range = new HttpRange(offset) };

        string result = await ExecuteAsync(
        func: async () =>
        {
            Response<BlobDownloadStreamingResult> response = await blobClient.DownloadStreamingAsync(
                    blobDownloadOptions,
                    cancellationToken);

            if (response == null || response.Value == null || response.Value.Content == null)
            {
                throw new RawResourceStoreException($"Failed to read resource from blob storage for storage identifier: {storageIdentifier} with offset {offset}");
            }

            string resource;

            using (Stream blobStream = response.Value.Content)
            {
                // The blob content is returned as a stream, so we need to read it into a string.
                using StreamReader reader = new StreamReader(blobStream, Encoding.UTF8);

                // Read to the end of line
                resource = await reader.ReadLineAsync();

                if (string.IsNullOrEmpty(resource))
                {
                    throw new RawResourceStoreException($"Failed to read line from blob storage for storage identifier: {storageIdentifier} with offset {offset}");
                }
            }

            return resource;
        },
        operationName: nameof(ReadRawResourceAsync));

        timer.Stop();
        _logger.LogInformation($"Successfully read raw resource from blob storage for storage identifier: {blobClient.Name} in {timer.ElapsedMilliseconds} ms");
        return new RawResource(result, Core.Models.FhirResourceFormat.Json, false);
    }

    public async Task<IReadOnlyList<RawResource>> ReadRawResourcesAsync(IList<ResourceWrapper> rawResources, CancellationToken cancellationToken)
    {
        EnsureArg.IsNotNull(rawResources, nameof(rawResources));

        Stopwatch timer = Stopwatch.StartNew();
        _logger.LogInformation($"Reading {rawResources.Count} raw resources from blob storage");

        var completeRawResources = new List<RawResource>();
        var blobOffsetDictionary = GetBlobOffsetCombinations(rawResources);

        foreach (var blobContainer in blobOffsetDictionary)
        {
            // if offset list is more than 1, read entire file, otherwise, just get the line
            if (blobContainer.Value.Count > 1)
            {
                // read the entire file
                var fileStream = await ReadBlobContainer(blobContainer.Key, 0, cancellationToken);
                completeRawResources.AddRange(await FindResourcesInStreamAsync(fileStream, blobContainer.Key, blobContainer.Value));
            }
            else
            {
                // read the line for 1 resource
                var rawResource = await ReadRawResourceAsync(blobContainer.Key, blobContainer.Value[0], cancellationToken);
                if (rawResource != null)
                {
                    completeRawResources.Add(rawResource);
                }
            }
        }

        timer.Stop();
        _logger.LogInformation($"Successfully read {rawResources.Count} raw resources from blob storage in {timer.ElapsedMilliseconds} ms");

        return completeRawResources;
    }

    internal static async Task<IReadOnlyList<RawResource>> FindResourcesInStreamAsync(Stream completeBlobStream, long storageIdentifier, IList<int> offsets)
    {
        var completeRawResourcesInBlob = new List<RawResource>();
        Stream blobStream = completeBlobStream;
        StreamReader reader = new StreamReader(blobStream, Encoding.UTF8);

        foreach (int offset in offsets)
        {
            // Set reading position to offset
            blobStream.Seek(offset, SeekOrigin.Begin);

            // The blob content is returned as a stream, so we need to read it into a string.
            // Read to the end of line
            string resource = await reader.ReadLineAsync();

            if (string.IsNullOrEmpty(resource))
            {
                throw new RawResourceStoreException($"Failed to read line from blob storage for storage identifier: {storageIdentifier} with offset {offset}");
            }

            completeRawResourcesInBlob.Add(new RawResource(resource, Core.Models.FhirResourceFormat.Json, false));
        }

        reader.Dispose();

        // await blobStream.Dispose();

        return completeRawResourcesInBlob.AsReadOnly();
    }

    protected async Task<Stream> ReadBlobContainer(long storageIdentifier, int offset, CancellationToken cancellationToken)
    {
        var blobContainerName = GetNewInstanceBlockBlobClient(storageIdentifier);

        BlockBlobClient blobClient = GetNewInstanceBlockBlobClient(storageIdentifier);
        var blobDownloadOptions = new BlobDownloadOptions { Range = new HttpRange(offset) };

        Stream result = await ExecuteAsync(
        func: async () =>
        {
            Response<BlobDownloadStreamingResult> response = await blobClient.DownloadStreamingAsync(
                    blobDownloadOptions,
                    cancellationToken);

            if (response == null || response.Value == null || response.Value.Content == null)
            {
                throw new RawResourceStoreException($"Failed to read resource from blob storage for storage identifier: {storageIdentifier} with offset {offset}");
            }

            return response.Value.Content;
        },
        operationName: nameof(ReadBlobContainer));

        return result;
    }

    internal static Dictionary<long, List<int>> GetBlobOffsetCombinations(IList<ResourceWrapper> rawResources)
    {
        // Dictionary containing different blobContainers that need to be read and the list of offsets within the blob
        var fileStorageDictionary = new Dictionary<long, List<int>>();

        foreach (var resource in rawResources)
        {
            if (resource.ResourceStorageIdentifier != -1 && resource.ResourceStorageOffset >= 0)
            {
                if (!fileStorageDictionary.TryGetValue(resource.ResourceStorageIdentifier, out List<int> value))
                {
                    value = new List<int>();
                    fileStorageDictionary[resource.ResourceStorageIdentifier] = value;
                }

                value.Add(resource.ResourceStorageOffset);
            }
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

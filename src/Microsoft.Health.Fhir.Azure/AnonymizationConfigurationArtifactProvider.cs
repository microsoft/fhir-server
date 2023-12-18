// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using EnsureThat;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData;
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData.Models;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.TemplateManagement.ArtifactProviders;
using Microsoft.Health.Fhir.TemplateManagement.Client;
using Microsoft.Health.Fhir.TemplateManagement.Exceptions;
using Microsoft.Health.Fhir.TemplateManagement.Models;
using Microsoft.Health.Fhir.TemplateManagement.Utilities;

namespace Microsoft.Health.Fhir.Azure
{
    public class AnonymizationConfigurationArtifactProvider : IArtifactProvider, IDisposable
    {
        private bool _disposed = false;
        private const string AnonymizationContainer = "anonymization";

        private readonly MemoryCache _cache;
        private readonly ILogger<AnonymizationConfigurationArtifactProvider> _logger;
        private readonly IExportClientInitializer<BlobServiceClient> _exportClientInitializer;
        private readonly ExportJobConfiguration _exportJobConfiguration;
        private OciArtifactProvider _anonymizationConfigurationCollectionProvider;
        private IContainerRegistryTokenProvider _containerRegistryTokenProvider;
        private BlobServiceClient _blobClient;

        public AnonymizationConfigurationArtifactProvider(
            IExportClientInitializer<BlobServiceClient> exportClientInitializer,
            IContainerRegistryTokenProvider containerRegistryTokenProvider,
            IOptions<ExportJobConfiguration> exportJobConfiguration,
            ILogger<AnonymizationConfigurationArtifactProvider> logger)
        {
            EnsureArg.IsNotNull(containerRegistryTokenProvider, nameof(containerRegistryTokenProvider));
            EnsureArg.IsNotNull(exportClientInitializer, nameof(exportClientInitializer));
            EnsureArg.IsNotNull(exportJobConfiguration?.Value, nameof(exportJobConfiguration));

            _containerRegistryTokenProvider = containerRegistryTokenProvider;
            _exportClientInitializer = exportClientInitializer;
            _exportJobConfiguration = exportJobConfiguration.Value;
            _logger = logger;

            _cache = new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = _exportJobConfiguration.CacheSizeLimit,
            });
        }

        public async Task FetchAsync(ExportJobRecord exportJobRecord, Stream targetStream, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrEmpty(exportJobRecord.AnonymizationConfigurationLocation, nameof(exportJobRecord.AnonymizationConfigurationLocation));
            EnsureArg.IsNotNull(targetStream, nameof(targetStream));

            string acrImageReference = exportJobRecord.AnonymizationConfigurationCollectionReference;
            string configName = exportJobRecord.AnonymizationConfigurationLocation;
            string eTag = exportJobRecord.AnonymizationConfigurationFileETag;

            if (!string.IsNullOrEmpty(acrImageReference))
            {
               await FetchConfigurationFromACR(acrImageReference, configName, targetStream, cancellationToken);
            }
            else
            {
               await FetchConfigurationFromBlob(configName, eTag, targetStream, cancellationToken);
            }
        }

        private async Task FetchConfigurationFromACR(string acrImageReference, string configName, Stream targetStream, CancellationToken cancellationToken)
        {
            var accessToken = string.Empty;
            ImageInfo imageInfo = ImageInfo.CreateFromImageReference(acrImageReference);

            async Task<string> TokenEntryFactory(ICacheEntry entry)
            {
                var token = await _containerRegistryTokenProvider.GetTokenAsync(imageInfo.Registry, cancellationToken);
                entry.Size = token.Length;
                entry.AbsoluteExpiration = GetTokenAbsoluteExpiration(token);
                return token;
            }

            accessToken = await _cache.GetOrCreateAsync(GetCacheKey(imageInfo.Registry), TokenEntryFactory);

            try
            {
                AcrClient client = new AcrClient(imageInfo.Registry, accessToken);
                _anonymizationConfigurationCollectionProvider = new OciArtifactProvider(imageInfo, client);
                var acrImage = await _anonymizationConfigurationCollectionProvider.GetOciArtifactAsync(cancellationToken);

                var blobsSize = acrImage.Blobs.Count;
                bool configFound = false;
                for (var i = blobsSize - 1; i >= 0; i--)
                {
                    if (CheckConfigurationCollectionIsTooLarge(acrImage.Blobs[i].Content.LongLength))
                    {
                        throw new AnonymizationConfigurationFetchException(Resources.AnonymizationConfigurationCollectionTooLarge);
                    }

                    using var str = new MemoryStream(acrImage.Blobs[i].Content);
                    Dictionary<string, byte[]> blobsDict = StreamUtility.DecompressFromTarGz(str);
                    if (!blobsDict.TryGetValue(configName, out byte[] value))
                    {
                        continue;
                    }
                    else
                    {
                        configFound = true;
                        if (CheckConfigurationIsTooLarge(value.LongLength))
                        {
                            throw new AnonymizationConfigurationFetchException(Resources.AnonymizationConfigurationTooLarge);
                        }

                        using (var config = new MemoryStream(value))
                        {
                            await config.CopyToAsync(targetStream, cancellationToken);
                            break;
                        }
                    }
                }

                if (!configFound)
                {
                    throw new FileNotFoundException(message: string.Format(CultureInfo.InvariantCulture, Resources.AnonymizationConfigurationNotFound, configName));
                }
            }
            catch (ContainerRegistryAuthenticationException authEx)
            {
                // Remove token from cache when authentication failed.
                _cache.Remove(GetCacheKey(imageInfo.Registry));
                throw new ContainerRegistryNotAuthorizedException(string.Format(Resources.ContainerRegistryNotAuthorized, imageInfo.Registry), authEx);
            }
        }

        private async Task FetchConfigurationFromBlob(string configName, string eTag, Stream targetStream, CancellationToken cancellationToken)
        {
            eTag = AddDoubleQuotesIfMissing(eTag);

            BlobServiceClient blobClient = Connect();
            BlobContainerClient container = blobClient.GetBlobContainerClient(AnonymizationContainer);
            if (!await container.ExistsAsync(cancellationToken))
            {
                throw new FileNotFoundException(message: Resources.AnonymizationContainerNotFound);
            }

            BlobClient blob = container.GetBlobClient(configName);
            if (await blob.ExistsAsync(cancellationToken))
            {
                var blobProperties = await blob.GetPropertiesAsync(cancellationToken: cancellationToken);

                if (CheckConfigurationIsTooLarge(size: blobProperties.Value.ContentLength))
                {
                    throw new AnonymizationConfigurationFetchException(Resources.AnonymizationConfigurationTooLarge);
                }

                if (string.IsNullOrEmpty(eTag))
                {
                    await blob.DownloadToAsync(targetStream, cancellationToken);
                }
                else
                {
                    var blobDownloadToOptions = new BlobDownloadToOptions();
                    blobDownloadToOptions.Conditions = new BlobRequestConditions();
                    blobDownloadToOptions.Conditions.IfMatch = new ETag(eTag);
                    try
                    {
                        await blob.DownloadToAsync(targetStream, blobDownloadToOptions, cancellationToken);
                    }
                    catch (RequestFailedException ex)
                    {
                        throw new AnonymizationConfigurationFetchException(ex.Message, ex);
                    }
                }
            }
            else
            {
                throw new FileNotFoundException(message: string.Format(CultureInfo.InvariantCulture, Resources.AnonymizationConfigurationNotFound, configName));
            }
        }

        private static string AddDoubleQuotesIfMissing(string eTag)
        {
            if (string.IsNullOrWhiteSpace(eTag) || eTag.StartsWith('\"'))
            {
                return eTag;
            }

            return $"\"{eTag}\"";
        }

        private BlobServiceClient Connect()
        {
            if (_blobClient == null)
            {
                _blobClient = _exportClientInitializer.GetAuthorizedClient(_exportJobConfiguration);
            }

            return _blobClient;
        }

        private static bool CheckConfigurationIsTooLarge(long size) =>
            size > 1 * 1024 * 1024; // Max content length is 1 MB

        private static bool CheckConfigurationCollectionIsTooLarge(long size) =>
            size > 100 * 1024 * 1024; // Max content length is 100 MB

        private static DateTimeOffset GetTokenAbsoluteExpiration(string accessToken)
        {
            var defaultExpiration = DateTimeOffset.Now.AddMinutes(30);
            if (accessToken.StartsWith("bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var jwtTokenText = accessToken.Substring(7);
                var handler = new JwtSecurityTokenHandler();
                var jwtToken = handler.ReadToken(jwtTokenText) as JwtSecurityToken;

                // Add 5 minutes buffer in case of last minute expirations.
                return new DateTimeOffset(jwtToken.ValidTo).AddMinutes(-5);
            }

            return defaultExpiration;
        }

        private static string GetCacheKey(string registryServer)
        {
            return $"registry_{registryServer}";
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _cache?.Dispose();
            }

            _disposed = true;
        }
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DotLiquid;
using EnsureThat;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert.Models;
using Microsoft.Health.Fhir.Core.Messages.DataConvert;
using Microsoft.Health.Fhir.TemplateManagement;
using Microsoft.Health.Fhir.TemplateManagement.Exceptions;
using Microsoft.Health.Fhir.TemplateManagement.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.DataConvert
{
    public class ContainerRegistryTemplateProvider : IDataConvertTemplateProvider, IDisposable
    {
        private bool _disposed = false;
        private readonly IContainerRegistryTokenProvider _containerRegistryTokenProvider;
        private readonly ITemplateCollectionProviderFactory _templateCollectionProviderFactory;
        private readonly DataConvertConfiguration _dataConvertConfig;
        private readonly MemoryCache _cache;
        private readonly ILogger<ContainerRegistryTemplateProvider> _logger;

        public ContainerRegistryTemplateProvider(
            IContainerRegistryTokenProvider containerRegistryTokenProvider,
            IOptions<DataConvertConfiguration> dataConvertConfig,
            ILogger<ContainerRegistryTemplateProvider> logger)
        {
            EnsureArg.IsNotNull(containerRegistryTokenProvider, nameof(containerRegistryTokenProvider));
            EnsureArg.IsNotNull(dataConvertConfig, nameof(dataConvertConfig));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _containerRegistryTokenProvider = containerRegistryTokenProvider;
            _dataConvertConfig = dataConvertConfig.Value;
            _logger = logger;

            // Initialize cache and template collection provider factory
            _cache = new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = _dataConvertConfig.CacheSizeLimit,
            });
            _templateCollectionProviderFactory = new TemplateCollectionProviderFactory(_cache, Options.Create(_dataConvertConfig.TemplateCollectionOptions));
        }

        /// <summary>
        /// Fetch template collection from container registry or built-in archive.
        /// </summary>
        /// <param name="request">The data convert request which contains template reference.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the fetch operation.</param>
        /// <returns>Template collection.</returns>
        public async Task<List<Dictionary<string, Template>>> GetTemplateCollectionAsync(DataConvertRequest request, CancellationToken cancellationToken)
        {
            // We have embedded a default template collection in the templatemanagement package.
            // If the template collection is the default reference, we don't need to retrieve token.
            var accessToken = string.Empty;
            if (!request.IsDefaultTemplateReference)
            {
                _logger.LogInformation("Using a custom template collection for data conversion.");

                async Task<string> TokenEntryFactory(ICacheEntry entry)
                {
                    var token = await _containerRegistryTokenProvider.GetTokenAsync(request.RegistryServer, cancellationToken);
                    entry.Size = token.Length;
                    entry.AbsoluteExpirationRelativeToNow = _dataConvertConfig.ContainerRegistryTokenExpiration;
                    return token;
                }

                accessToken = await _cache.GetOrCreateAsync(GetCacheKey(request.RegistryServer), TokenEntryFactory);
            }
            else
            {
                _logger.LogInformation("Using the default template collection for data conversion.");
            }

            try
            {
                var provider = _templateCollectionProviderFactory.CreateTemplateCollectionProvider(request.TemplateCollectionReference, accessToken);
                return await provider.GetTemplateCollectionAsync(cancellationToken);
            }
            catch (ContainerRegistryAuthenticationException authEx)
            {
                // Remove token from cache when authentication failed.
                _cache.Remove(GetCacheKey(request.RegistryServer));

                _logger.LogError(authEx, "Failed to access container registry.");
                throw new ContainerRegistryNotAuthorizedException(string.Format(Resources.ContainerRegistryNotAuthorized, request.RegistryServer), authEx);
            }
            catch (ImageTooLargeException tooLargeException)
            {
                _logger.LogError(tooLargeException, "The template image is too large.");
                throw new TemplateCollectionTooLargeException(string.Format(Resources.TemplateImageTooLarge, _dataConvertConfig.TemplateCollectionOptions.TemplateCollectionSizeLimitMegabytes), tooLargeException);
            }
            catch (ImageNotFoundException notFoundException)
            {
                _logger.LogError(notFoundException, "The template image is not found.");
                throw new TemplateCollectionNotFoundException(string.Format(Resources.TemplateImageNotFound, request.TemplateCollectionReference), notFoundException);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception: failed to get template collection.");
                throw new FetchTemplateCollectionFailedException(string.Format(Resources.FetchTemplateCollectionFailed, ex.Message), ex);
            }
        }

        private static string GetCacheKey(string registryServer)
        {
            return string.Format("registry_{0}", registryServer);
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

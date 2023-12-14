﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Threading;
using System.Threading.Tasks;
using DotLiquid;
using EnsureThat;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData.Models;
using Microsoft.Health.Fhir.Core.Messages.ConvertData;
using Microsoft.Health.Fhir.TemplateManagement;
using Microsoft.Health.Fhir.TemplateManagement.ArtifactProviders;
using Microsoft.Health.Fhir.TemplateManagement.Exceptions;

namespace Microsoft.Health.Fhir.Core.Features.Operations.ConvertData
{
    public class ContainerRegistryTemplateProvider : IConvertDataTemplateProvider, IDisposable
    {
        private bool _disposed = false;
        private readonly IContainerRegistryTokenProvider _containerRegistryTokenProvider;
        private readonly TemplateCollectionProviderFactory _templateCollectionProviderFactory;
        private readonly MemoryCache _cache;
        private readonly MemoryCache _templateProviderCache;
        private readonly SemaphoreSlim _templateProviderFactorySemaphore;
        private readonly ILogger<ContainerRegistryTemplateProvider> _logger;

        public ContainerRegistryTemplateProvider(
            IContainerRegistryTokenProvider containerRegistryTokenProvider,
            IOptions<ConvertDataConfiguration> convertDataConfig,
            ILogger<ContainerRegistryTemplateProvider> logger)
        {
            EnsureArg.IsNotNull(containerRegistryTokenProvider, nameof(containerRegistryTokenProvider));
            EnsureArg.IsNotNull(convertDataConfig?.Value, nameof(convertDataConfig));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _containerRegistryTokenProvider = containerRegistryTokenProvider;
            _logger = logger;

            // Initialize cache and template collection provider factory
            _cache = new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = convertDataConfig.Value.CacheSizeLimit,
            });
            _templateCollectionProviderFactory = new TemplateCollectionProviderFactory(_cache, Options.Create(convertDataConfig.Value.TemplateCollectionOptions));

            _templateProviderCache = new MemoryCache(new MemoryCacheOptions());
            _templateProviderFactorySemaphore = new SemaphoreSlim(1, 1);
        }

        /// <summary>
        /// Fetch template collection from container registry following a custom template convert request.
        /// </summary>
        /// <param name="request">The convert data request which contains template reference.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the fetch operation.</param>
        /// <returns>Template collection.</returns>
        public async Task<List<Dictionary<string, Template>>> GetTemplateCollectionAsync(ConvertDataRequest request, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Using a custom template collection for data conversion.");

            async Task<string> TokenEntryFactory(ICacheEntry entry)
            {
                var token = await _containerRegistryTokenProvider.GetTokenAsync(request.RegistryServer, cancellationToken);
                entry.Size = token.Length;
                entry.AbsoluteExpiration = GetTokenAbsoluteExpiration(token);
                return token;
            }

            var accessToken = await _cache.GetOrCreateAsync(GetCacheKey(request.RegistryServer), TokenEntryFactory);

            try
            {
                /*
                   First try to get the template provider from the cache.
                   If template provider is not in cache, then limit the number of threads that can create new providers to 1, and then create a new provider.
                   This is to limit the amount of providers created in a multi-threaded scenario, so that we do not have multiple providers pulling the same image from the ACR.
                */
                ITemplateCollectionProvider provider = _templateProviderCache.Get(request.TemplateCollectionReference) as ITemplateCollectionProvider;

                if (provider == null)
                {
                    await _templateProviderFactorySemaphore.WaitAsync(cancellationToken);
                    try
                    {
                        provider = _templateProviderCache.Get(request.TemplateCollectionReference) as ITemplateCollectionProvider;
                        if (provider == null)
                        {
                            provider = _templateCollectionProviderFactory.CreateTemplateCollectionProvider(request.TemplateCollectionReference, accessToken);
                            _templateProviderCache.Set(request.TemplateCollectionReference, provider, TimeSpan.FromMinutes(5));
                        }
                    }
                    finally
                    {
                        _templateProviderFactorySemaphore.Release();
                    }
                }

                return await provider.GetTemplateCollectionAsync(cancellationToken);
            }
            catch (ContainerRegistryAuthenticationException authEx)
            {
                // Remove token from cache when authentication failed.
                _cache.Remove(GetCacheKey(request.RegistryServer));

                _logger.LogWarning(authEx, "Failed to access container registry");
                throw new ContainerRegistryNotAuthorizedException(string.Format(Core.Resources.ContainerRegistryNotAuthorized, request.RegistryServer), authEx);
            }
            catch (ImageFetchException fetchEx)
            {
                _logger.LogWarning(fetchEx, "Failed to fetch template image");
                throw new FetchTemplateCollectionFailedException(string.Format(Core.Resources.FetchTemplateCollectionFailed, fetchEx.Message), fetchEx);
            }
            catch (TemplateManagementException templateEx)
            {
                _logger.LogWarning(templateEx, "Template collection is invalid");
                throw new TemplateCollectionErrorException(string.Format(Core.Resources.FetchTemplateCollectionFailed, templateEx.Message), templateEx);
            }
            catch (Exception unhandledEx)
            {
                _logger.LogError(unhandledEx, "Unhandled exception: failed to get template collection");
                throw new FetchTemplateCollectionFailedException(string.Format(Core.Resources.FetchTemplateCollectionFailed, unhandledEx.Message), unhandledEx);
            }
        }

        /// <summary>
        /// Try to parse exp claim from the acr JWT token. Return 30 minutes as default expiration.
        /// </summary>
        /// <param name="accessToken">JWT token with "Bearer" prefix.</param>
        /// <returns>Expiration DateTimeOffset.</returns>
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
                _templateProviderCache?.Dispose();
                _templateProviderFactorySemaphore?.Dispose();
            }

            _disposed = true;
        }
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.ContainerRegistry;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Microsoft.Health.Fhir.TemplateManagement.Client;
using Microsoft.Health.Fhir.TemplateManagement.Models;

namespace Microsoft.Health.Fhir.Azure.ContainerRegistry
{
    public class AzureContainerRegistryClientInitializer : IExportClientInitializer<AzureContainerRegistryClient>, IDisposable
    {
        private bool _disposed = false;
        private readonly IContainerRegistryTokenProvider _containerRegistryTokenProvider;
        private readonly ExportJobConfiguration _exportJobConfiguration;
        private readonly MemoryCache _cache;
        private readonly ILogger<AzureContainerRegistryClientInitializer> _logger;

        public AzureContainerRegistryClientInitializer(
            IContainerRegistryTokenProvider containerRegistryTokenProvider,
            IOptions<ExportJobConfiguration> exportJobConfiguration,
            ILogger<AzureContainerRegistryClientInitializer> logger)
        {
            EnsureArg.IsNotNull(exportJobConfiguration?.Value, nameof(exportJobConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _containerRegistryTokenProvider = containerRegistryTokenProvider;
            _exportJobConfiguration = exportJobConfiguration.Value;
            _logger = logger;

            _cache = new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = _exportJobConfiguration.CacheSizeLimit,
            });
        }

        public Task<AzureContainerRegistryClient> GetAuthorizedClientAsync(CancellationToken cancellationToken)
        {
            return GetAuthorizedClientAsync(_exportJobConfiguration, cancellationToken);
        }

        public Task<AzureContainerRegistryClient> GetAuthorizedClientAsync(ExportJobConfiguration exportJobConfiguration, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(exportJobConfiguration.AcrServer))
            {
                throw null;
            }

            var accessToken = string.Empty;
            _logger.LogInformation("Get token for Acr Client.");

            async Task<string> TokenEntryFactory(ICacheEntry entry)
            {
                var token = await _containerRegistryTokenProvider.GetTokenAsync(exportJobConfiguration.AcrServer, cancellationToken);
                entry.Size = token.Length;
                entry.AbsoluteExpiration = GetTokenAbsoluteExpiration(token);
                return token;
            }

            accessToken = _cache.GetOrCreateAsync(GetCacheKey(exportJobConfiguration.AcrServer), TokenEntryFactory).Result;

            AzureContainerRegistryClient acrClient = null;
            try
            {
                // string token = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{registryUsername}:{registryPassword}"));

                acrClient = new AzureContainerRegistryClient(exportJobConfiguration.AcrServer, new ACRClientCredentials(accessToken));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create a ACR Client");

                throw new ExportClientInitializerException(Resources.InvalidConnectionSettings, HttpStatusCode.BadRequest);
            }

            return Task.FromResult(acrClient);
        }

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

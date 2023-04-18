using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Threading;
using System.Threading.Tasks;
using DotLiquid;
using EnsureThat;
using Microsoft.Azure.ContainerRegistry.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData.Models;
using Microsoft.Health.Fhir.Core.Messages.ConvertData;
using Microsoft.Health.Fhir.TemplateManagement;
using Microsoft.Health.Fhir.TemplateManagement.Exceptions;

namespace Microsoft.Health.Fhir.Core.Features.Operations.ConvertData
{
    public class DefaultTemplateProvider : IConvertDataTemplateProvider, IDisposable
    {
        private bool _disposed = false;
        private readonly ITemplateCollectionProviderFactory _templateCollectionProviderFactory;
        private readonly ConvertDataConfiguration _convertDataConfig;

        public DefaultTemplateProvider(
            IOptions<ConvertDataConfiguration> convertDataConfig,
            ILogger<ContainerRegistryTemplateProvider> logger)
        {
            EnsureArg.IsNotNull(convertDataConfig, nameof(convertDataConfig));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _convertDataConfig = convertDataConfig.Value;

            Logger = logger;

            // Initialize cache and template collection provider factory
            Cache = new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = _convertDataConfig.CacheSizeLimit,
            });
            _templateCollectionProviderFactory = new TemplateCollectionProviderFactory(Cache, Options.Create(_convertDataConfig.TemplateCollectionOptions));
        }

        protected MemoryCache Cache { get; }

        protected ILogger<ContainerRegistryTemplateProvider> Logger { get; }

        /// <summary>
        /// Fetch template collection from container registry or built-in archive.
        /// </summary>
        /// <param name="request">The convert data request which contains template reference.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the fetch operation.</param>
        /// <returns>Template collection.</returns>
        public virtual async Task<List<Dictionary<string, Template>>> GetTemplateCollectionAsync(ConvertDataRequest request, CancellationToken cancellationToken)
        {
            // We have embedded a default template collection in the templatemanagement package.
            // If the template collection is the default reference, we don't need to retrieve token.
            var accessToken = string.Empty;
            if (!request.IsDefaultTemplateReference)
            {
                throw new ContainerRegistryAuthenticationException("External Managed Identity not configured.");
            }
            else
            {
                Logger.LogInformation("Using the default template collection for data conversion.");
            }

            return await GetTemplatesFromRequestAsync(request, accessToken, cancellationToken);
        }

        /// <summary>
        /// Fetch template collection from container registry or built-in archive given a request and an access token.
        /// </summary>
        /// <param name="request">The convert data request which contains template reference.</param>
        /// <param name="accessToken">The token used to access a container registry. Can be empty for a default template request.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the fetch operation.</param>
        /// <returns>Template collection.</returns>
        protected async Task<List<Dictionary<string, Template>>> GetTemplatesFromRequestAsync(ConvertDataRequest request, string accessToken, CancellationToken cancellationToken)
        {
            try
            {
                var provider = _templateCollectionProviderFactory.CreateTemplateCollectionProvider(request.TemplateCollectionReference, accessToken);
                return await provider.GetTemplateCollectionAsync(cancellationToken);
            }
            catch (ContainerRegistryAuthenticationException authEx)
            {
                // Remove token from cache when authentication failed.
                Cache.Remove(GetCacheKey(request.RegistryServer));

                Logger.LogWarning(authEx, "Failed to access container registry.");
                throw new ContainerRegistryNotAuthorizedException(string.Format(Core.Resources.ContainerRegistryNotAuthorized, request.RegistryServer), authEx);
            }
            catch (ImageFetchException fetchEx)
            {
                Logger.LogWarning(fetchEx, "Failed to fetch template image.");
                throw new FetchTemplateCollectionFailedException(string.Format(Core.Resources.FetchTemplateCollectionFailed, fetchEx.Message), fetchEx);
            }
            catch (TemplateManagementException templateEx)
            {
                Logger.LogWarning(templateEx, "Template collection is invalid.");
                throw new TemplateCollectionErrorException(string.Format(Core.Resources.FetchTemplateCollectionFailed, templateEx.Message), templateEx);
            }
            catch (Exception unhandledEx)
            {
                Logger.LogError(unhandledEx, "Unhandled exception: failed to get template collection.");
                throw new FetchTemplateCollectionFailedException(string.Format(Core.Resources.FetchTemplateCollectionFailed, unhandledEx.Message), unhandledEx);
            }
        }

        protected virtual string SetToken(ConvertDataRequest request, CancellationToken cancellationToken)
        {
            // We have embedded a default template collection in the templatemanagement package.
            // If the template collection is the default reference, we don't need to retrieve token.
            var accessToken = string.Empty;
            if (!request.IsDefaultTemplateReference)
            {
                throw new ContainerRegistryAuthenticationException("External Managed Identity not configured.");
            }
            else
            {
                Logger.LogInformation("Using the default template collection for data conversion.");
            }

            return accessToken;
        }

        protected static string GetCacheKey(string registryServer)
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
                Cache?.Dispose();
            }

            _disposed = true;
        }
    }
}

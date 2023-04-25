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
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData.Models;
using Microsoft.Health.Fhir.Core.Messages.ConvertData;
using Microsoft.Health.Fhir.TemplateManagement;
using Microsoft.Health.Fhir.TemplateManagement.Exceptions;

namespace Microsoft.Health.Fhir.Core.Features.Operations.ConvertData
{
    public class DefaultTemplateProvider : IConvertDataTemplateProvider, IDisposable
    {
        private bool _disposed = false;
        private readonly ILogger _logger;
        private readonly MemoryCache _cache;
        private readonly ITemplateCollectionProviderFactory _templateCollectionProviderFactory;
        private readonly ConvertDataConfiguration _convertDataConfig;

        public DefaultTemplateProvider(
            IOptions<ConvertDataConfiguration> convertDataConfig,
            ILogger<IConvertDataTemplateProvider> logger)
        {
            EnsureArg.IsNotNull(convertDataConfig, nameof(convertDataConfig));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _convertDataConfig = convertDataConfig.Value;

            _logger = logger;

            // Initialize cache and template collection provider factory
            _cache = new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = _convertDataConfig.CacheSizeLimit,
            });
            _templateCollectionProviderFactory = new TemplateCollectionProviderFactory(_cache, Options.Create(_convertDataConfig.TemplateCollectionOptions));
        }

        /// <summary>
        /// Fetch template collection from container registry or built-in archive.
        /// </summary>
        /// <param name="request">The convert data request which contains template reference.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the fetch operation.</param>
        /// <returns>Template collection.</returns>
        public async Task<List<Dictionary<string, Template>>> GetTemplateCollectionAsync(ConvertDataRequest request, CancellationToken cancellationToken)
        {
            var accessToken = string.Empty;

            _logger.LogInformation("Using the default template collection for data conversion.");

            try
            {
                var provider = _templateCollectionProviderFactory.CreateTemplateCollectionProvider(request.TemplateCollectionReference, accessToken);
                return await provider.GetTemplateCollectionAsync(cancellationToken);
            }
            catch (TemplateManagementException templateEx)
            {
                _logger.LogWarning(templateEx, "Template collection is invalid.");
                throw new TemplateCollectionErrorException(string.Format(Core.Resources.FetchTemplateCollectionFailed, templateEx.Message), templateEx);
            }
            catch (Exception unhandledEx)
            {
                _logger.LogError(unhandledEx, "Unhandled exception: failed to get template collection.");
                throw new FetchTemplateCollectionFailedException(string.Format(Core.Resources.FetchTemplateCollectionFailed, unhandledEx.Message), unhandledEx);
            }
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

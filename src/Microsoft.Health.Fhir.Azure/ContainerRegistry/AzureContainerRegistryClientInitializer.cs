// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Microsoft.Health.Fhir.TemplateManagement.Client;

namespace Microsoft.Health.Fhir.Azure.ContainerRegistry
{
    public class AzureContainerRegistryClientInitializer : IClientInitializer<ACRClient>
    {
        private readonly IContainerRegistryTokenProvider _containerRegistryTokenProvider;
        private readonly ExportJobConfiguration _exportJobConfiguration;
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
        }

        public Task<ACRClient> GetAuthorizedClientAsync(CancellationToken cancellationToken)
        {
            return GetAuthorizedClientAsync(_exportJobConfiguration, cancellationToken);
        }

        public Task<ACRClient> GetAuthorizedClientAsync(ExportJobConfiguration exportJobConfiguration, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(exportJobConfiguration.AcrServer))
            {
                throw null;
            }

            _logger.LogInformation("Get token for Acr Client.");
            var accessToken = _containerRegistryTokenProvider.GetTokenAsync(exportJobConfiguration.AcrServer, cancellationToken).Result;

            ACRClient acrClient = null;
            try
            {
                // string token = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{registryUsername}:{registryPassword}"));

                acrClient = new ACRClient(exportJobConfiguration.AcrServer, accessToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create a ACR Client");

                throw new ExportClientInitializerException(Resources.InvalidConnectionSettings, HttpStatusCode.BadRequest);
            }

            return Task.FromResult(acrClient);
        }
    }
}

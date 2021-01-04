// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.ContainerRegistry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Microsoft.Health.Fhir.TemplateManagement.Client;
using Microsoft.Health.Fhir.TemplateManagement.Models;

namespace Microsoft.Health.Fhir.Azure.ContainerRegistry
{
    public class AzureContainerRegistryClientInitializer : IExportClientInitializer<AzureContainerRegistryClient>
    {
        private readonly ExportJobConfiguration _exportJobConfiguration;
        private readonly ILogger<AzureContainerRegistryClientInitializer> _logger;

        public AzureContainerRegistryClientInitializer(IOptions<ExportJobConfiguration> exportJobConfiguration, ILogger<AzureContainerRegistryClientInitializer> logger)
        {
            EnsureArg.IsNotNull(exportJobConfiguration?.Value, nameof(exportJobConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _exportJobConfiguration = exportJobConfiguration.Value;
            _logger = logger;
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

            AzureContainerRegistryClient acrClient = null;
            try
            {
                string[] registryInfo = exportJobConfiguration.AcrServer.Split(":");
                string registryServer = registryInfo[0];
                string registryPassword = registryInfo[1];
                string registryUsername = registryServer.Split('.')[0];

                string imageReference = string.Format("{0}/{1}:{2}", registryServer, "acrtest", "onelayer");
                ImageInfo imageInfo = ImageInfo.CreateFromImageReference(imageReference);
                string token = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{registryUsername}:{registryPassword}"));

                acrClient = new AzureContainerRegistryClient(imageInfo.Registry, new ACRClientCredentials(token));
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

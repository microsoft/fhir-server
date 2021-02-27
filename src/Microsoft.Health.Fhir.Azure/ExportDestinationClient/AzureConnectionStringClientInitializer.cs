// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;

namespace Microsoft.Health.Fhir.Azure.ExportDestinationClient
{
    public class AzureConnectionStringClientInitializer : IExportClientInitializer<BlobServiceClient>
    {
        private readonly ExportJobConfiguration _exportJobConfiguration;
        private readonly ILogger<AzureConnectionStringClientInitializer> _logger;

        public AzureConnectionStringClientInitializer(IOptions<ExportJobConfiguration> exportJobConfiguration, ILogger<AzureConnectionStringClientInitializer> logger)
        {
            EnsureArg.IsNotNull(exportJobConfiguration?.Value, nameof(exportJobConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _exportJobConfiguration = exportJobConfiguration.Value;
            _logger = logger;
        }

        public Task<BlobServiceClient> GetAuthorizedClientAsync(CancellationToken cancellationToken)
        {
            return GetAuthorizedClientAsync(_exportJobConfiguration, cancellationToken);
        }

        public Task<BlobServiceClient> GetAuthorizedClientAsync(ExportJobConfiguration exportJobConfiguration, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(exportJobConfiguration.StorageAccountConnection))
            {
                throw new ExportClientInitializerException(Resources.InvalidConnectionSettings, HttpStatusCode.BadRequest);
            }

            BlobServiceClient blobClient;
            try
            {
                blobClient = new BlobServiceClient(exportJobConfiguration.StorageAccountConnection);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create a Cloud Blob Client");

                throw new ExportClientInitializerException(Resources.InvalidConnectionSettings, HttpStatusCode.BadRequest);
            }

            return Task.FromResult(blobClient);
        }
    }
}

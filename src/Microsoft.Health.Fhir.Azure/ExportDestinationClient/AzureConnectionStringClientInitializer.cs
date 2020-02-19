// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;

namespace Microsoft.Health.Fhir.Azure.ExportDestinationClient
{
    public class AzureConnectionStringClientInitializer : IExportClientInitializer<CloudBlobClient>
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

        public Task<CloudBlobClient> GetAuthorizedClientAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_exportJobConfiguration.StorageAccountConnection))
            {
                throw new ExportClientInitializerException(Resources.InvalidConnectionSettings, HttpStatusCode.BadRequest);
            }

            if (!CloudStorageAccount.TryParse(_exportJobConfiguration.StorageAccountConnection, out CloudStorageAccount cloudAccount))
            {
                throw new ExportClientInitializerException(Resources.InvalidConnectionSettings, HttpStatusCode.BadRequest);
            }

            CloudBlobClient blobClient = null;
            try
            {
                blobClient = cloudAccount.CreateCloudBlobClient();
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

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using Azure.Identity;
using Azure.Storage.Blobs;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;

namespace Microsoft.Health.Fhir.Azure.ExportDestinationClient
{
    public class AzureAccessTokenClientInitializer : IExportClientInitializer<BlobServiceClient>
    {
        private readonly ExportJobConfiguration _exportJobConfiguration;
        private readonly ILogger<AzureAccessTokenClientInitializer> _logger;

        public AzureAccessTokenClientInitializer(
            IOptions<ExportJobConfiguration> exportJobConfiguration,
            ILogger<AzureAccessTokenClientInitializer> logger)
        {
            EnsureArg.IsNotNull(exportJobConfiguration?.Value, nameof(exportJobConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _exportJobConfiguration = exportJobConfiguration.Value;
            _logger = logger;
        }

        public BlobServiceClient GetAuthorizedClient()
        {
            return GetAuthorizedClient(_exportJobConfiguration);
        }

        public BlobServiceClient GetAuthorizedClient(ExportJobConfiguration exportJobConfiguration)
        {
            // Get storage uri from config
            if (string.IsNullOrWhiteSpace(exportJobConfiguration.StorageAccountUri))
            {
                throw new ExportClientInitializerException(Resources.InvalidStorageUri, HttpStatusCode.BadRequest);
            }

            if (!Uri.TryCreate(exportJobConfiguration.StorageAccountUri, UriKind.Absolute, out Uri storageAccountUri))
            {
                throw new ExportClientInitializerException(Resources.InvalidStorageUri, HttpStatusCode.BadRequest);
            }

            try
            {
                return new BlobServiceClient(connectionString: exportJobConfiguration.StorageAccountConnection);
            }
            catch (AccessTokenProviderException atp)
            {
                _logger.LogError(atp, "Unable to get access token");

                throw new ExportClientInitializerException(Resources.CannotGetAccessToken, HttpStatusCode.Unauthorized);
            }
        }
    }
}

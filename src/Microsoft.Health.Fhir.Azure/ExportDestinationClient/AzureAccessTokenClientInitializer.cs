// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Storage.Auth;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;

namespace Microsoft.Health.Fhir.Azure.ExportDestinationClient
{
    public class AzureAccessTokenClientInitializer : IExportClientInitializer<CloudBlobClient>
    {
        private readonly IAccessTokenProvider _accessTokenProvider;
        private readonly ExportJobConfiguration _exportJobConfiguration;
        private readonly ILogger<AzureAccessTokenClientInitializer> _logger;

        public AzureAccessTokenClientInitializer(
            IAccessTokenProvider accessTokenProvider,
            IOptions<ExportJobConfiguration> exportJobConfiguration,
            ILogger<AzureAccessTokenClientInitializer> logger)
        {
            EnsureArg.IsNotNull(accessTokenProvider, nameof(accessTokenProvider));
            EnsureArg.IsNotNull(exportJobConfiguration?.Value, nameof(exportJobConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _accessTokenProvider = accessTokenProvider;
            _exportJobConfiguration = exportJobConfiguration.Value;
            _logger = logger;
        }

        public async Task<CloudBlobClient> GetAuthorizedClientAsync(CancellationToken cancellationToken)
        {
            return await GetAuthorizedClientAsync(_exportJobConfiguration, cancellationToken);
        }

        public async Task<CloudBlobClient> GetAuthorizedClientAsync(ExportJobConfiguration exportJobConfiguration, CancellationToken cancellationToken)
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

            string accessToken = null;
            try
            {
                accessToken = await _accessTokenProvider.GetAccessTokenForResourceAsync(storageAccountUri, cancellationToken);
            }
            catch (AccessTokenProviderException atp)
            {
                _logger.LogError(atp, "Unable to get access token");

                throw new ExportClientInitializerException(Resources.CannotGetAccessToken, HttpStatusCode.Unauthorized);
            }

            using var tokenCredential = new TokenCredential(accessToken);

            var storageCredentials = new StorageCredentials(tokenCredential);
            return new CloudBlobClient(storageAccountUri, storageCredentials);
        }
    }
}

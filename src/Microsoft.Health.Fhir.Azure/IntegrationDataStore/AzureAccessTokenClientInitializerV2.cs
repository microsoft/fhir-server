// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.Storage.Blobs;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;

namespace Microsoft.Health.Fhir.Azure.IntegrationDataStore
{
    public class AzureAccessTokenClientInitializerV2 : IIntegrationDataStoreClientInitilizer<BlobServiceClient>
    {
        private readonly IntegrationDataStoreConfiguration _integrationDataStoreConfiguration;
        private readonly ILogger<AzureAccessTokenClientInitializerV2> _logger;

        public AzureAccessTokenClientInitializerV2(
            IOptions<IntegrationDataStoreConfiguration> integrationDataStoreConfiguration,
            ILogger<AzureAccessTokenClientInitializerV2> logger)
        {
            EnsureArg.IsNotNull(integrationDataStoreConfiguration?.Value, nameof(integrationDataStoreConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _integrationDataStoreConfiguration = integrationDataStoreConfiguration.Value;
            _logger = logger;
        }

        public async Task<BlobServiceClient> GetAuthorizedClientAsync()
        {
            return await GetAuthorizedClientAsync(_integrationDataStoreConfiguration);
        }

        public Task<BlobServiceClient> GetAuthorizedClientAsync(IntegrationDataStoreConfiguration integrationDataStoreConfiguration)
        {
            if (string.IsNullOrWhiteSpace(integrationDataStoreConfiguration.StorageAccountUri))
            {
                throw new IntegrationDataStoreClientInitializerException(Resources.InvalidStorageUri, HttpStatusCode.BadRequest);
            }

            if (!Uri.TryCreate(integrationDataStoreConfiguration.StorageAccountUri, UriKind.Absolute, out Uri storageAccountUri))
            {
                throw new IntegrationDataStoreClientInitializerException(Resources.InvalidStorageUri, HttpStatusCode.BadRequest);
            }

            try
            {
                return Task.FromResult(new BlobServiceClient(storageAccountUri, new DefaultAzureCredential()));
            }
            catch (AccessTokenProviderException atp)
            {
                _logger.LogError(atp, "Unable to get access token");

                throw new IntegrationDataStoreClientInitializerException(Resources.CannotGetAccessToken, HttpStatusCode.Unauthorized);
            }
        }
    }
}

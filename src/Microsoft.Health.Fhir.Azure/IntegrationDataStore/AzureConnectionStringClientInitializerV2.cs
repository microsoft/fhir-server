// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;

namespace Microsoft.Health.Fhir.Azure.IntegrationDataStore
{
    public class AzureConnectionStringClientInitializerV2 : IIntegrationDataStoreClientInitilizer<BlobServiceClient>
    {
        private readonly IntegrationDataStoreConfiguration _integrationDataStoreConfiguration;
        private readonly ILogger<AzureConnectionStringClientInitializerV2> _logger;

        public AzureConnectionStringClientInitializerV2(IOptions<IntegrationDataStoreConfiguration> integrationDataStoreConfiguration, ILogger<AzureConnectionStringClientInitializerV2> logger)
        {
            EnsureArg.IsNotNull(integrationDataStoreConfiguration?.Value, nameof(integrationDataStoreConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _integrationDataStoreConfiguration = integrationDataStoreConfiguration.Value;
            _logger = logger;
        }

        public Task<BlobServiceClient> GetAuthorizedClientAsync()
        {
            return GetAuthorizedClientAsync(_integrationDataStoreConfiguration);
        }

        public Task<BlobServiceClient> GetAuthorizedClientAsync(IntegrationDataStoreConfiguration integrationDataStoreConfiguration)
        {
            if (string.IsNullOrWhiteSpace(integrationDataStoreConfiguration.StorageAccountConnection))
            {
                throw new IntegrationDataStoreClientInitializerException(Resources.InvalidConnectionSettings, HttpStatusCode.BadRequest);
            }

            BlobServiceClient blobClient = null;
            try
            {
                blobClient = new BlobServiceClient(integrationDataStoreConfiguration.StorageAccountConnection);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create a Cloud Blob Client");

                throw new IntegrationDataStoreClientInitializerException(Resources.InvalidConnectionSettings, HttpStatusCode.BadRequest);
            }

            return Task.FromResult(blobClient);
        }
    }
}

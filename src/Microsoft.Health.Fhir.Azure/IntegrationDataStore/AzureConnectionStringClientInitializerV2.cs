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
using Microsoft.Health.Fhir.Core.Features.Operations;

namespace Microsoft.Health.Fhir.Azure.IntegrationDataStore
{
    public class AzureConnectionStringClientInitializerV2 : IIntegrationDataStoreClientInitilizer<CloudBlobClient>
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

        public Task<CloudBlobClient> GetAuthorizedClientAsync(CancellationToken cancellationToken)
        {
            return GetAuthorizedClientAsync(_integrationDataStoreConfiguration, cancellationToken);
        }

        public Task<CloudBlobClient> GetAuthorizedClientAsync(IntegrationDataStoreConfiguration integrationDataStoreConfiguration, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(integrationDataStoreConfiguration.StorageAccountConnection))
            {
                throw new IntegrationDataStoreClientInitializerException(Resources.InvalidConnectionSettings, HttpStatusCode.BadRequest);
            }

            if (!CloudStorageAccount.TryParse(integrationDataStoreConfiguration.StorageAccountConnection, out CloudStorageAccount cloudAccount))
            {
                throw new IntegrationDataStoreClientInitializerException(Resources.InvalidConnectionSettings, HttpStatusCode.BadRequest);
            }

            CloudBlobClient blobClient;
            try
            {
                blobClient = cloudAccount.CreateCloudBlobClient();
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

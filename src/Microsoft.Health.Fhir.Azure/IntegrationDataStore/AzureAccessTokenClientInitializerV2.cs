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
using Microsoft.Health.Fhir.Core.Features.Operations;

namespace Microsoft.Health.Fhir.Azure.IntegrationDataStore
{
    public class AzureAccessTokenClientInitializerV2 : IIntegrationDataStoreClientInitilizer<CloudBlobClient>
    {
        private readonly IAccessTokenProvider _accessTokenProvider;
        private readonly IntegrationDataStoreConfiguration _integrationDataStoreConfiguration;
        private readonly ILogger<AzureAccessTokenClientInitializerV2> _logger;

        public AzureAccessTokenClientInitializerV2(
            IAccessTokenProvider accessTokenProvider,
            IOptions<IntegrationDataStoreConfiguration> integrationDataStoreConfiguration,
            ILogger<AzureAccessTokenClientInitializerV2> logger)
        {
            EnsureArg.IsNotNull(accessTokenProvider, nameof(accessTokenProvider));
            EnsureArg.IsNotNull(integrationDataStoreConfiguration?.Value, nameof(integrationDataStoreConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _accessTokenProvider = accessTokenProvider;
            _integrationDataStoreConfiguration = integrationDataStoreConfiguration.Value;
            _logger = logger;
        }

        public async Task<CloudBlobClient> GetAuthorizedClientAsync(CancellationToken cancellationToken)
        {
            return await GetAuthorizedClientAsync(_integrationDataStoreConfiguration, cancellationToken);
        }

        public async Task<CloudBlobClient> GetAuthorizedClientAsync(IntegrationDataStoreConfiguration integrationDataStoreConfiguration, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(integrationDataStoreConfiguration.StorageAccountUri))
            {
                throw new IntegrationDataStoreClientInitializerException(Resources.InvalidStorageUri, HttpStatusCode.BadRequest);
            }

            if (!Uri.TryCreate(integrationDataStoreConfiguration.StorageAccountUri, UriKind.Absolute, out Uri storageAccountUri))
            {
                throw new IntegrationDataStoreClientInitializerException(Resources.InvalidStorageUri, HttpStatusCode.BadRequest);
            }

            string accessToken;
            try
            {
                accessToken = await _accessTokenProvider.GetAccessTokenForResourceAsync(storageAccountUri, cancellationToken);
            }
            catch (AccessTokenProviderException atp)
            {
                _logger.LogError(atp, "Unable to get access token");

                throw new IntegrationDataStoreClientInitializerException(Resources.CannotGetAccessToken, HttpStatusCode.Unauthorized);
            }

#pragma warning disable CA2000 // Dispose objects before losing scope
            StorageCredentials storageCredentials = new StorageCredentials(new TokenCredential(accessToken));
#pragma warning restore CA2000 // Dispose objects before losing scope
            return new CloudBlobClient(storageAccountUri, storageCredentials);
        }
    }
}

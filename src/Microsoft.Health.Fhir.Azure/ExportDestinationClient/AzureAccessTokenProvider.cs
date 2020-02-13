// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;

namespace Microsoft.Health.Fhir.Azure.ExportDestinationClient
{
    public class AzureAccessTokenProvider : IAccessTokenProvider
    {
        private readonly AzureServiceTokenProvider _azureServiceTokenProvider;
        private readonly ILogger<AzureAccessTokenProvider> _logger;

        public AzureAccessTokenProvider(ILogger<AzureAccessTokenProvider> logger)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));

            _azureServiceTokenProvider = new AzureServiceTokenProvider();
            _logger = logger;
        }

        public async Task<string> GetAccessTokenForResourceAsync(Uri resourceUri, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resourceUri, nameof(resourceUri));

            string accessToken = await _azureServiceTokenProvider.GetAccessTokenAsync(resourceUri.ToString(), cancellationToken: cancellationToken);
            if (accessToken == null)
            {
                _logger.LogWarning("Failed to retrieve access token");

                throw new AccessTokenProviderException(Resources.CannotGetAccessToken);
            }
            else
            {
                _logger.LogInformation("Successfully retrieved access token");
            }

            return accessToken;
        }
    }
}

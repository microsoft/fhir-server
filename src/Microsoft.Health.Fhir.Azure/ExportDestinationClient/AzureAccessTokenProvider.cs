// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations;

namespace Microsoft.Health.Fhir.Azure.ExportDestinationClient
{
    public class AzureAccessTokenProvider : IAccessTokenProvider
    {
        private readonly DefaultAzureCredential _azureServiceTokenProvider;
        private readonly ILogger<AzureAccessTokenProvider> _logger;

        public AzureAccessTokenProvider(ILogger<AzureAccessTokenProvider> logger)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));

            _azureServiceTokenProvider = new DefaultAzureCredential();
            _logger = logger;
        }

        public async Task<string> GetAccessTokenForResourceAsync(Uri resourceUri, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resourceUri, nameof(resourceUri));

            // https://learn.microsoft.com/en-us/dotnet/api/overview/azure/app-auth-migration?view=azure-dotnet
            var accessTokenContext = new TokenRequestContext(scopes: new[] { resourceUri + "/.default" });

            AccessToken accessToken;
            try
            {
                accessToken = await _azureServiceTokenProvider.GetTokenAsync(accessTokenContext, cancellationToken: cancellationToken);
            }
            catch (CredentialUnavailableException ex)
            {
                _logger.LogWarning(ex, "Failed to retrieve access token");
                throw new AccessTokenProviderException(Resources.CannotGetAccessToken);
            }

            if (string.IsNullOrEmpty(accessToken.Token))
            {
                _logger.LogWarning("Failed to retrieve access token");

                throw new AccessTokenProviderException(Resources.CannotGetAccessToken);
            }
            else
            {
                _logger.LogInformation("Successfully retrieved access token");
            }

            return accessToken.Token;
        }
    }
}

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
using Microsoft.Health.Fhir.Core.Features.Operations.Export.AccessTokenProvider;

namespace Microsoft.Health.Fhir.Azure
{
    public class AzureAccessTokenProvider : IAccessTokenProvider
    {
        private readonly AzureServiceTokenProvider _azureTokenProvider;
        private readonly ILogger<AzureAccessTokenProvider> _logger;

        public AzureAccessTokenProvider(ILogger<AzureAccessTokenProvider> logger)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));

            _azureTokenProvider = new AzureServiceTokenProvider();
            _logger = logger;
        }

        public string AccessTokenProviderType => "azure";

        public async Task<string> GetAccessTokenForResourceAsync(Uri resourceUri, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resourceUri, nameof(resourceUri));

            _logger.LogInformation($"Received request to get access token for uri: {resourceUri}");
            string accessToken = await _azureTokenProvider.GetAccessTokenAsync(resourceUri.ToString(), cancellationToken: cancellationToken);

            _logger.LogInformation($"Access token received: {accessToken}");
            return accessToken;
        }
    }
}

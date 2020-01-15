// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public class ManagedIdentityAccessTokenProvider : IAccessTokenProvider
    {
        private readonly AzureServiceTokenProvider _azureTokenProvider;
        private readonly ILogger<ManagedIdentityAccessTokenProvider> _logger;

        public ManagedIdentityAccessTokenProvider(ILogger<ManagedIdentityAccessTokenProvider> logger)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));

            _azureTokenProvider = new AzureServiceTokenProvider();
            _logger = logger;
        }

        public async Task<string> GetAccessTokenForResourceAsync(Uri resourceUri)
        {
            EnsureArg.IsNotNull(resourceUri, nameof(resourceUri));

            _logger.LogInformation($"Received request to get access token for uri: {resourceUri}");
            string accessToken = await _azureTokenProvider.GetAccessTokenAsync("https://storage.azure.com/");

            _logger.LogInformation($"Access token received: {accessToken}");
            return accessToken;
        }
    }
}

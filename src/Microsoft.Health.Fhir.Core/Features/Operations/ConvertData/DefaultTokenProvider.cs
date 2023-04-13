// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData;

namespace Microsoft.Health.Fhir.Azure.ContainerRegistry
{
    /// <summary>
    /// Retrieve ACR access token with AAD token provider.
    /// We need to exchange ACR refresh token with AAD token, and get ACR access token from refresh token.
    /// References:
    /// https://github.com/Azure/acr/blob/main/docs/AAD-OAuth.md#calling-post-oauth2exchange-to-get-an-acr-refresh-token
    /// https://github.com/Azure/acr/blob/main/docs/AAD-OAuth.md#calling-post-oauth2token-to-get-an-acr-access-token
    /// </summary>
    public class DefaultTokenProvider : IContainerRegistryTokenProvider
    {
        private readonly ILogger<DefaultTokenProvider> _logger;

        public DefaultTokenProvider(ILogger<DefaultTokenProvider> logger)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));

            _logger = logger;
        }

        public Task<string> GetTokenAsync(string registryServer, CancellationToken cancellationToken) // TODO: SHAUN - will this method being syncronous cause an issue?
        {
            _logger.LogInformation("Accessing default token provider");

            return null;
        }
    }
}

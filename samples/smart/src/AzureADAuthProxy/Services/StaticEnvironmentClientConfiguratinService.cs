// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Configuration;
using AzureADAuthProxy.Configuration;
using Microsoft.Extensions.Options;

namespace AzureADAuthProxy.Services
{
    public class StaticEnvironmentClientConfiguratinService : IClientConfigService
    {
        private readonly AzureADProxyConfig _config;

        public StaticEnvironmentClientConfiguratinService(AzureADProxyConfig config)
        {
            _config = config;
        }

        public Task<BackendClientConfiguration> FetchBackendClientConfiguration(string clientId)
        {
            if (string.IsNullOrEmpty(_config.TestBackendClientId) || string.IsNullOrEmpty(_config.TestBackendClientSecret) || string.IsNullOrEmpty(_config.TestBackendClientJwks))
            {
                throw new ConfigurationErrorsException("Invalid configuration for StaticEnvironmentClientConfiguratinService");
            }

            if (clientId != _config.TestBackendClientId)
            {
                throw new UnauthorizedAccessException("Client id does not match static configuration.");
            }

            var data = new BackendClientConfiguration(_config.TestBackendClientId!, _config.TestBackendClientSecret!, _config.TestBackendClientJwks!);

            return Task.FromResult(data);
        }
    }
}

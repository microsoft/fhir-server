// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Configuration;
using SMARTCustomOperations.AzureAuth.Configuration;
using SMARTCustomOperations.AzureAuth.Services;

namespace AzureADAutSMARTCustomOperations.AzureAuthhProxy.Services
{
    public class StaticEnvironmentClientConfiguratinService : IClientConfigService
    {
        private readonly AzureAuthOperationsConfig _config;

        public StaticEnvironmentClientConfiguratinService(AzureAuthOperationsConfig config)
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

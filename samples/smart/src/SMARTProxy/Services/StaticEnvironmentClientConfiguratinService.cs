// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Configuration;
using SMARTProxy.Configuration;

namespace SMARTProxy.Services
{
    public class StaticEnvironmentClientConfiguratinService : IClientConfigService
    {
        public Task<BackendClientConfiguration> FetchBackendClientConfiguration(SMARTProxyConfig config, string clientId)
        {
            if (string.IsNullOrEmpty(config.TestBackendClientId) || string.IsNullOrEmpty(config.TestBackendClientSecret) || string.IsNullOrEmpty(config.TestBackendClientJwks))
            {
                throw new ConfigurationErrorsException("Invalid configuration for StaticEnvironmentClientConfiguratinService");
            }

            var data = new BackendClientConfiguration(config.TestBackendClientId!, config.TestBackendClientSecret!, config.TestBackendClientJwks!);

            return Task.FromResult(data);
        }
    }
}

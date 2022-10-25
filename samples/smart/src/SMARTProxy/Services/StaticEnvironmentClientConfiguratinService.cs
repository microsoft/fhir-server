// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using SMARTProxy.Configuration;

namespace SMARTProxy.Services
{
    public class StaticEnvironmentClientConfiguratinService : IClientConfigService
    {
        public Task<BackendClientConfiguration> FetchBackendClientConfiguration(SMARTProxyConfig config, string clientId)
        {
            var data = new BackendClientConfiguration(config.TestBackendClientId!, config.TestBackendClientSecret!, config.TestBackendClientJwks!);

            return Task.FromResult(data);
        }
    }
}

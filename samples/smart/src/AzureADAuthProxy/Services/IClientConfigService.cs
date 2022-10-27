// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace AzureADAuthProxy.Services
{
    public interface IClientConfigService
    {
        Task<BackendClientConfiguration> FetchBackendClientConfiguration(string clientId);
    }
}

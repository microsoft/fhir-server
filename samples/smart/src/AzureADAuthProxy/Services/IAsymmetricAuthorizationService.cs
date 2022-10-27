// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace AzureADAuthProxy.Services
{
    public interface IAsymmetricAuthorizationService
    {
        public Task<BackendClientConfiguration> AuthenticateBackendAsyncClient(string clientId, string clientAssertion);
    }
}

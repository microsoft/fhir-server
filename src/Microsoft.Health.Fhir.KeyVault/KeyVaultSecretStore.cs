// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Health.Fhir.Core.Features.SecretStore;

namespace Microsoft.Health.Fhir.KeyVault
{
    public class KeyVaultSecretStore : ISecretStore
    {
        private IKeyVaultClient _keyVaultClient;
        private Uri _keyVaultUri;

        public KeyVaultSecretStore(IKeyVaultClient keyVaultClient, Uri keyVaultUri)
        {
            EnsureArg.IsNotNull(keyVaultClient, nameof(keyVaultClient));
            EnsureArg.IsNotNull(keyVaultUri, nameof(keyVaultUri));

            _keyVaultClient = keyVaultClient;
            _keyVaultUri = keyVaultUri;
        }

        public async Task<SecretWrapper> GetSecretAsync(string secretName)
        {
            EnsureArg.IsNotNullOrWhiteSpace(secretName);

            SecretBundle result = await _keyVaultClient.GetSecretAsync(_keyVaultUri.AbsoluteUri, secretName);

            return new SecretWrapper(result.Id, result.Value);
        }

        public async Task<SecretWrapper> SetSecretAsync(string secretName, string secretValue)
        {
            EnsureArg.IsNotNullOrWhiteSpace(secretName, nameof(secretName));
            EnsureArg.IsNotNullOrWhiteSpace(secretValue, nameof(secretValue));

            SecretBundle result = await _keyVaultClient.SetSecretAsync(_keyVaultUri.AbsoluteUri, secretName, secretValue);

            return new SecretWrapper(result.Id, result.Value);
        }

        public async Task<SecretWrapper> DeleteSecretAsync(string secretName)
        {
            EnsureArg.IsNotNullOrWhiteSpace(secretName, nameof(secretName));

            SecretBundle result = await _keyVaultClient.DeleteSecretAsync(_keyVaultUri.AbsoluteUri, secretName);

            return new SecretWrapper(result.Id, result.Value);
        }
    }
}

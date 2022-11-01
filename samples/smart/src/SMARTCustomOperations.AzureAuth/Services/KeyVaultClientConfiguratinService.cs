// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Configuration;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging;
using SMARTCustomOperations.AzureAuth.Configuration;

namespace SMARTCustomOperations.AzureAuth.Services
{
    public class KeyVaultClientConfiguratinService : IClientConfigService
    {
        private readonly string _vaultName;
        private readonly string _vaultUrl;
        private readonly ILogger _logger;

        public KeyVaultClientConfiguratinService(AzureAuthOperationsConfig config, ILogger<AsymmetricAuthorizationService> logger)
        {
            if (string.IsNullOrEmpty(config.BackendServiceKeyVaultStore))
            {
                throw new ConfigurationErrorsException("BackendServiceKeyVaultStore must be set to use the KeyVaultClientConfiguratinService.");
            }

            _vaultName = config.BackendServiceKeyVaultStore!;
            _vaultUrl = "https://" + config.BackendServiceKeyVaultStore! + ".vault.azure.net";
            _logger = logger;
        }

        public async Task<BackendClientConfiguration> FetchBackendClientConfiguration(string clientId)
        {
            _logger.LogInformation("Attempting to pull backend service client informatoin for {ClientId}", clientId);

            try
            {
                // Create a keyvault client and try to fetch the client info that corresponds to the request
                var client = new SecretClient(new Uri(_vaultUrl), new DefaultAzureCredential());

                var secret = await client.GetSecretAsync(clientId);

                // Create a backend client config from the vault
                var data = new BackendClientConfiguration(clientId, secret.Value.Value, secret.Value.Properties.Tags["jwks_url"]);

                return data;
            }
            catch (Azure.RequestFailedException ex)
            {
                if (ex.Status == 401)
                {
                    _logger.LogCritical("Application is not setup correctly. Please provide application access to KeyVault via Managed Identity.");
                    throw new ConfigurationErrorsException($"The function app is not correctly configured to access KeyVault.", ex);
                }

                if (ex.Status == 404)
                {
                    throw new UnauthorizedAccessException($"KeyVault could not find the secret with the name {clientId}", ex);
                }

                _logger.LogError("Unexpected error encountered while accessing KeyVault for client {ClientId}", ex);
                throw new UnauthorizedAccessException($"KeyVault error finding the client with the name {clientId}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError("Unexpected error encountered while accessing KeyVault for client {ClientId}", ex);
                throw new UnauthorizedAccessException($"KeyVault error when trying to {clientId}", ex);
            }
        }
    }
}

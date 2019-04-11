// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.SecretStore;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.KeyVault;
using Microsoft.Health.Fhir.KeyVault.Configs;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class FhirServerBuilderSecretStoreRegistrationExtensions
    {
        private const string KeyVaultConfigurationName = "KeyVault";

        public static IFhirServerBuilder AddSecretStore(this IFhirServerBuilder fhirServerBuilder, IConfiguration configuration)
        {
            EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            var keyVaultConfig = new KeyVaultConfiguration();
            configuration.GetSection(KeyVaultConfigurationName).Bind(keyVaultConfig);

            if (string.IsNullOrWhiteSpace(keyVaultConfig.EndPoint))
            {
                fhirServerBuilder.Services.Add<InMemorySecretStore>()
                .Singleton()
                .AsService<ISecretStore>();
            }
            else
            {
                fhirServerBuilder.Services.Add<KeyVaultSecretStore>((sp) =>
                {
                    var tokenProvider = new AzureServiceTokenProvider();
                    var kvClient = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(tokenProvider.KeyVaultTokenCallback));

                    return new KeyVaultSecretStore(kvClient, new Uri(keyVaultConfig.EndPoint));
                })
                .Singleton()
                .AsService<ISecretStore>();
            }

            return fhirServerBuilder;
        }
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Azure.ExportDestinationClient;
using Microsoft.Health.Fhir.Azure.KeyVault;
using Microsoft.Health.Fhir.Azure.KeyVault.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Features.SecretStore;
using Microsoft.Health.Fhir.Core.Registration;

namespace Microsoft.Health.Fhir.Azure
{
    public static class FhirServerBuilderAzureRegistrationExtensions
    {
        private const string KeyVaultConfigurationName = "KeyVault";

        public static IFhirServerBuilder AddAzureExportDestinationClient(this IFhirServerBuilder fhirServerBuilder)
        {
            EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));

            fhirServerBuilder.Services.Add<AzureExportDestinationClient>()
                .Transient()
                .AsSelf();

            fhirServerBuilder.Services.Add<Func<IExportDestinationClient>>(sp => () => sp.GetRequiredService<AzureExportDestinationClient>())
                .Transient()
                .AsSelf();

            return fhirServerBuilder;
        }

        public static IFhirServerBuilder AddKeyVaultSecretStore(this IFhirServerBuilder fhirServerBuilder, IConfiguration configuration)
        {
            EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            // Get the KeyVault endpoint mentioned in the config. It is not necessary the KeyVault config
            // section is present (depending on how we choose to implement ISecretStore). But even if it is
            // not present, GetSection will return an empty IConfigurationSection. And we will end up creating
            // an InMemoryKeyVaultSecretStore in that scenario also.
            var keyVaultConfig = new KeyVaultConfiguration();
            configuration.GetSection(KeyVaultConfigurationName).Bind(keyVaultConfig);

            if (string.IsNullOrWhiteSpace(keyVaultConfig.Endpoint))
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

                    return new KeyVaultSecretStore(kvClient, new Uri(keyVaultConfig.Endpoint));
                })
                    .Singleton()
                    .AsService<ISecretStore>();
            }

            return fhirServerBuilder;
        }
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Azure.Storage.Blobs;
using EnsureThat;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Azure.ContainerRegistry;
using Microsoft.Health.Fhir.Azure.ExportDestinationClient;
using Microsoft.Health.Fhir.Azure.IntegrationDataStore;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Registration;

namespace Microsoft.Health.Fhir.Azure
{
    public static class FhirServerBuilderAzureRegistrationExtensions
    {
        private const string ExportConfigurationName = "FhirServer:Operations:Export";
        private const string IntegrationDataStoreConfigurationName = "FhirServer:Operations:IntegrationDataStore";

        public static IFhirServerBuilder AddAzureExportDestinationClient(this IFhirServerBuilder fhirServerBuilder)
        {
            EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));

            fhirServerBuilder.Services.Add<AzureExportDestinationClient>()
                .Transient()
                .AsService<IExportDestinationClient>();

            fhirServerBuilder.Services.Add<AnonymizationConfigurationArtifactProvider>()
                .Transient()
                .AsService<IArtifactProvider>();

            return fhirServerBuilder;
        }

        public static IFhirServerBuilder AddAzureExportClientInitializer(this IFhirServerBuilder fhirServerBuilder, IConfiguration configuration)
        {
            EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            var exportJobConfiguration = new ExportJobConfiguration();
            configuration.GetSection(ExportConfigurationName).Bind(exportJobConfiguration);

            if (!string.IsNullOrWhiteSpace(exportJobConfiguration.StorageAccountUri))
            {
                fhirServerBuilder.Services.Add<AzureAccessTokenClientInitializer>()
                    .Transient()
                    .AsService<IExportClientInitializer<BlobServiceClient>>();

                fhirServerBuilder.Services.Add<AzureAccessTokenProvider>()
                    .Transient()
                    .AsService<IAccessTokenProvider>();
            }
            else
            {
                fhirServerBuilder.Services.Add<AzureConnectionStringClientInitializer>()
                    .Transient()
                    .AsService<IExportClientInitializer<BlobServiceClient>>();
            }

            return fhirServerBuilder;
        }

        public static IFhirServerBuilder AddContainerRegistryTokenProvider(this IFhirServerBuilder fhirServerBuilder)
        {
            EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));

            fhirServerBuilder.Services.Add<AzureAccessTokenProvider>()
                .Transient()
                .AsService<IAccessTokenProvider>();
            fhirServerBuilder.Services.Add<AzureContainerRegistryAccessTokenProvider>()
                .Singleton()
                .AsService<IContainerRegistryTokenProvider>();

            return fhirServerBuilder;
        }

        public static IFhirServerBuilder AddContainerRegistryAccessValidator(this IFhirServerBuilder fhirServerBuilder)
        {
            EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));

            fhirServerBuilder.Services.Add<BaseContainerRegistryAccessValidator>()
                .Transient()
                .AsService<IContainerRegistryAccessValidator>();

            return fhirServerBuilder;
        }

        /// <summary>
        /// Customer can use this DataStore to integrate with other Azure services for data purpose.
        /// </summary>
        /// <param name="fhirServerBuilder">Service builder for FHIR server</param>
        /// <param name="configuration">Configuration for FHIR server</param>
        public static IFhirServerBuilder AddAzureIntegrationDataStoreClient(this IFhirServerBuilder fhirServerBuilder, IConfiguration configuration)
        {
            EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));

            var integrationDataStoreConfiguration = new IntegrationDataStoreConfiguration();
            configuration.GetSection(IntegrationDataStoreConfigurationName).Bind(integrationDataStoreConfiguration);

            if (!string.IsNullOrWhiteSpace(integrationDataStoreConfiguration.StorageAccountUri))
            {
                fhirServerBuilder.Services.Add<AzureAccessTokenClientInitializerV2>()
                    .Transient()
                    .AsService<IIntegrationDataStoreClientInitilizer<CloudBlobClient>>();

                fhirServerBuilder.Services.Add<AzureAccessTokenProvider>()
                    .Transient()
                    .AsService<IAccessTokenProvider>();
            }
            else
            {
                fhirServerBuilder.Services.Add<AzureConnectionStringClientInitializerV2>()
                .Transient()
                .AsService<IIntegrationDataStoreClientInitilizer<CloudBlobClient>>();
            }

            fhirServerBuilder.Services.Add<AzureBlobIntegrationDataStoreClient>()
                .Transient()
                .AsImplementedInterfaces();

            return fhirServerBuilder;
        }
    }
}

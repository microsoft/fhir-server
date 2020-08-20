// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Azure.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Registration;

namespace Microsoft.Health.Fhir.Azure
{
    public static class FhirServerBuilderAzureRegistrationExtensions
    {
        private const string ExportConfigurationName = "FhirServer:Operations:Export";

        public static IFhirServerBuilder AddAzureExportDestinationClient(this IFhirServerBuilder fhirServerBuilder)
        {
            EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));

            fhirServerBuilder.Services.Add<AzureExportDestinationClient>()
                .Transient()
                .AsService<IExportDestinationClient>();

            fhirServerBuilder.Services.Add<ExportDestinationArtifactProvider>()
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
                    .AsService<IExportClientInitializer<CloudBlobClient>>();

                fhirServerBuilder.Services.Add<AzureAccessTokenProvider>()
                    .Transient()
                    .AsService<IAccessTokenProvider>();
            }
            else
            {
                fhirServerBuilder.Services.Add<AzureConnectionStringClientInitializer>()
                    .Transient()
                    .AsService<IExportClientInitializer<CloudBlobClient>>();
            }

            return fhirServerBuilder;
        }
    }
}

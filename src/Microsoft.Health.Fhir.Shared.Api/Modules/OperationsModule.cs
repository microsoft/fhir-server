// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Medino;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Everything;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Messages.Storage;
using Microsoft.Health.Fhir.FirelySdk.Features.Operations.Import;
using Microsoft.Health.Fhir.Shared.Core.Features.Operations.Import;
#if NET10_0_OR_GREATER
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Ignixa.Features.Operations.Import;
#endif

namespace Microsoft.Health.Fhir.Api.Modules
{
    /// <summary>
    /// Registration of operations components.
    /// </summary>
    public class OperationsModule : IStartupModule
    {
        private readonly FhirSdkProvider _fhirSdkProvider;

        /// <summary>
        /// Initializes a new instance of the <see cref="OperationsModule"/> class.
        /// </summary>
        /// <param name="fhirServerConfiguration">The FHIR server configuration, used to select the configured FHIR SDK provider.</param>
        public OperationsModule(FhirServerConfiguration fhirServerConfiguration)
        {
            EnsureArg.IsNotNull(fhirServerConfiguration, nameof(fhirServerConfiguration));

            _fhirSdkProvider = fhirServerConfiguration.CoreFeatures.FhirSdkProvider;
        }

        public void Load(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            services.Add<GroupMemberExtractor>()
                .Singleton()
                .AsService<IGroupMemberExtractor>();

            services.Add<ExportJobTask>()
                .Transient()
                .AsSelf();

            services.Add<IExportJobTask>(sp => sp.GetRequiredService<ExportJobTask>())
                .Transient()
                .AsSelf()
                .AsFactory();

            services.Add<ResourceToNdjsonBytesSerializer>()
                .Singleton()
                .AsService<IResourceToByteArraySerializer>();

            services.AddSingleton<IPatientEverythingService, PatientEverythingService>();

            services.Add<ImportResourceLoader>()
                .Transient()
                .AsSelf()
                .AsImplementedInterfaces();

            switch (_fhirSdkProvider)
            {
                case FhirSdkProvider.Firely:
                    services.Add<FirelyImportResourceParser>()
                        .Transient()
                        .AsSelf()
                        .AsImplementedInterfaces();
                    break;

#if NET10_0_OR_GREATER
                case FhirSdkProvider.Ignixa:
                    services.Add<IgnixaSchemaContext>()
                        .Singleton()
                        .AsSelf();

                    services.Add<IgnixaImportResourceParser>()
                        .Transient()
                        .AsSelf()
                        .AsImplementedInterfaces();
                    break;
#else
                case FhirSdkProvider.Ignixa:
                    throw new NotSupportedException(
                        "FhirSdkProvider.Ignixa requires net10.0. Configure Firely when running net8.0.");
#endif

                default:
                    throw new InvalidOperationException($"Unsupported FHIR SDK provider: {_fhirSdkProvider}.");
            }

            services.Add<FhirSdkProviderStartupLogger>()
                .Singleton()
                .AsService<IHostedService>();

            services.Add<ImportErrorStoreFactory>()
                .Transient()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<ImportErrorSerializer>()
                .Transient()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<AzureAccessTokenProvider>()
                .Transient()
                .AsService<IAccessTokenProvider>();
        }
    }
}

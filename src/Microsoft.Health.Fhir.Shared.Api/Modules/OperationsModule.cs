// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;

namespace Microsoft.Health.Fhir.Api.Modules
{
    /// <summary>
    /// Registration of operations components.
    /// </summary>
    public class OperationsModule : IStartupModule
    {
        public void Load(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            services.Add<ExportDestinationClientFactory>()
                .Singleton()
                .AsService<IExportDestinationClientFactory>();

            services.Add<InMemoryExportDestinationClient>()
                .Transient()
                .AsSelf();

            services.Add<AzureExportDestinationClient>()
                .Transient()
                .AsSelf();

            services.Add<IExportDestinationClient>(sp => sp.GetRequiredService<AzureExportDestinationClient>())
                .Transient()
                .AsSelf()
                .AsFactory();

            services.Add<ExportJobTask>()
                .Transient()
                .AsSelf();

            services.Add<IExportJobTask>(sp => sp.GetRequiredService<ExportJobTask>())
                .Transient()
                .AsSelf()
                .AsFactory();

            services.Add<ExportJobWorker>()
                .Singleton()
                .AsSelf();

            services.Add<ResourceToNdjsonBytesSerializer>()
                .Singleton()
                .AsService<IResourceToByteArraySerializer>();
        }
    }
}

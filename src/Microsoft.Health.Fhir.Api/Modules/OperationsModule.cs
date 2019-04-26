// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;

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

            services.Add<ExportJobTaskFactory>()
                .Singleton()
                .AsService<IExportJobTaskFactory>();

            services.Add<ExportJobWorker>()
                .Singleton()
                .AsSelf();

            services.AddHostedService<ExportJobWorkerBackgroundService>();
        }
    }
}

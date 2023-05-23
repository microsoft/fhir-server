// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Features.Operations;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Operations.Everything;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Messages.Storage;
using Microsoft.Health.Fhir.Shared.Core.Features.Operations.Import;

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

            services
                .RemoveServiceTypeExact<ExportJobWorker, INotificationHandler<StorageInitializedNotification>>()
                .Add<ExportJobWorker>()
                .Singleton()
                .AsSelf()
                .AsService<INotificationHandler<StorageInitializedNotification>>();

            services.Add<ResourceToNdjsonBytesSerializer>()
                .Singleton()
                .AsService<IResourceToByteArraySerializer>();

            services.Add<IReindexJobTask>(sp => sp.GetRequiredService<ReindexJobTask>())
                .Transient()
                .AsSelf()
                .AsFactory();

            services
                .RemoveServiceTypeExact<ReindexJobWorker, INotificationHandler<SearchParametersInitializedNotification>>()
                .Add<ReindexJobWorker>()
                .Singleton()
                .AsSelf()
                .AsService<INotificationHandler<SearchParametersInitializedNotification>>();

            services.AddSingleton<IReindexUtilities, ReindexUtilities>();

            services.AddSingleton<IPatientEverythingService, PatientEverythingService>();

            services.Add<ImportResourceLoader>()
                .Transient()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<ImportResourceParser>()
                .Transient()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<ImportErrorStoreFactory>()
                .Transient()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<ImportErrorSerializer>()
                .Transient()
                .AsSelf()
                .AsImplementedInterfaces();
        }
    }
}

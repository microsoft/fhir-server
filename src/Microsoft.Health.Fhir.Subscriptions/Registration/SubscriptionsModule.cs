// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Subscriptions;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Storage;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Subscriptions.Channels;
using Microsoft.Health.Fhir.Subscriptions.HeartBeats;
using Microsoft.Health.Fhir.Subscriptions.Models;
using Microsoft.Health.Fhir.Subscriptions.Persistence;
using Microsoft.Health.Fhir.Subscriptions.Validation;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Subscriptions.Registration
{
    public class SubscriptionsModule : IStartupModule
    {
        private readonly CoreFeatureConfiguration _coreFeatureConfiguration;

        public SubscriptionsModule(CoreFeatureConfiguration coreFeatureConfiguration)
        {
            _coreFeatureConfiguration = EnsureArg.IsNotNull(coreFeatureConfiguration, nameof(coreFeatureConfiguration));
        }

        public void Load(IServiceCollection services)
        {
            if (!_coreFeatureConfiguration.SupportsSubscriptions)
            {
                return;
            }

            IEnumerable<TypeRegistrationBuilder> jobs = services.TypesInSameAssemblyAs<SubscriptionsModule>()
                .AssignableTo<IJob>()
                .Transient()
                .AsSelf();

            foreach (TypeRegistrationBuilder job in jobs)
            {
                job.AsDelegate<Func<IJob>>();
            }

            services
                .RemoveServiceTypeExact<SubscriptionManager, INotificationHandler<StorageInitializedNotification>>()
                .Add<SubscriptionManager>()
                .Singleton()
                .AsSelf()
                .AsImplementedInterfaces();

            services.TypesInSameAssemblyAs<ISubscriptionChannel>()
                .AssignableTo<ISubscriptionChannel>()
                .Transient()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<StorageChannelFactory>()
                .Singleton()
                .AsSelf();

            services.Add<ISubscriptionModelConverter>(c =>
            {
                switch (c.GetService<IModelInfoProvider>().Version)
                {
                    case FhirSpecification.R4:
                        return new SubscriptionModelConverterR4();
                    default:
                        throw new BadRequestException("Version not supported");
                }
            })
            .Singleton()
            .AsSelf()
            .AsImplementedInterfaces();

            services.Add<SubscriptionUpdator>()
                .Singleton()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<SubscriptionValidator>()
                .Singleton()
                .AsSelf()
                .AsImplementedInterfaces();

            services.RemoveServiceTypeExact<HeartBeatBackgroundService, INotificationHandler<StorageInitializedNotification>>() // Mediatr registers handlers as Transient by default, this extension ensures these aren't still there, only needed when service != Transient
               .Add<HeartBeatBackgroundService>()
               .Singleton()
               .AsSelf()
               .AsImplementedInterfaces();

            services.AddTransient(typeof(IPipelineBehavior<CreateResourceRequest, UpsertResourceResponse>), typeof(CreateOrUpdateSubscriptionBehavior<CreateResourceRequest, UpsertResourceResponse>));
            services.AddTransient(typeof(IPipelineBehavior<UpsertResourceRequest, UpsertResourceResponse>), typeof(CreateOrUpdateSubscriptionBehavior<UpsertResourceRequest, UpsertResourceResponse>));
        }
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Core.Features.Resources.Upsert;

namespace Microsoft.Health.Fhir.Api.Modules.FeatureFlags.ConditionalUpdates
{
    public class ConditionalUpsertFeatureModule : IStartupModule
    {
        public void Load(IServiceCollection services)
        {
            services.Add<ConditionalUpsertResourceHandler.IsEnabled>(x => () => x.GetRequiredService<IOptions<FeatureConfiguration>>().Value.SupportsConditionalUpdate)
                .Singleton()
                .AsSelf();

            services.Add<ConditionalUpsertPostConfigureOptions>()
                .Singleton()
                .AsSelf()
                .AsService<IPostConfigureOptions<MvcOptions>>();
        }
    }
}

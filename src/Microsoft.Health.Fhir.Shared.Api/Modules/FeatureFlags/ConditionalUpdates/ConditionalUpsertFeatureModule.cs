// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;

namespace Microsoft.Health.Fhir.Api.Modules.FeatureFlags.ConditionalUpdates
{
    public class ConditionalUpsertFeatureModule : IStartupModule
    {
        public void Load(IServiceCollection services)
        {
            services.Add<ConditionalUpsertPostConfigureOptions>()
                .Singleton()
                .AsSelf()
                .AsService<IPostConfigureOptions<MvcOptions>>();
        }
    }
}

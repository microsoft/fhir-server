// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Core.Features.Resources.Create;

namespace Microsoft.Health.Fhir.Api.Modules.FeatureFlags.ConditionalUpdates
{
    public class ConditionalCreateFeatureModule : IStartupModule
    {
        public void Load(IServiceCollection services)
        {
            services.Add<ConditionalCreateResourceHandler.IsEnabled>(x => () => x.GetRequiredService<IOptions<FeatureConfiguration>>().Value.SupportsConditionalCreate)
                .Singleton()
                .AsSelf();

            services.Add<ConditionalCreatePostConfigureOptions>()
                .Singleton()
                .AsSelf()
                .AsService<IPostConfigureOptions<MvcOptions>>();
        }
    }
}

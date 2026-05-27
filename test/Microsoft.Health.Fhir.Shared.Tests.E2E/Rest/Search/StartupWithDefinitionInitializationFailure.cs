// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    /// <summary>
    /// Startup that causes the definition stage to fail by replacing ISearchParameterStatusDataStore
    /// with a stub that always throws. The definition manager calls GetSearchParameterStatuses()
    /// during LoadSearchParamsFromDataStore(), so this prevents the definition stage from completing.
    /// </summary>
    public class StartupWithDefinitionInitializationFailure : StartupBaseForCustomProviders
    {
        public StartupWithDefinitionInitializationFailure(IConfiguration configuration)
            : base(configuration)
        {
        }

        public override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);

            services.Replace(ServiceDescriptor.Singleton<ISearchParameterStatusDataStore, AlwaysFailingSearchParameterStatusDataStore>());
        }
    }
}

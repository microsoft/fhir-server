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
    /// Startup that causes the status manager stage to fail by replacing ISearchParameterStatusManager
    /// with one that throws when handling SearchParameterDefinitionManagerInitialized.
    /// This allows the definition stage to complete but prevents the status stage from completing.
    /// </summary>
    public class StartupWithStatusInitializationFailure : StartupBaseForCustomProviders
    {
        public StartupWithStatusInitializationFailure(IConfiguration configuration)
            : base(configuration)
        {
        }

        public override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);

            services.Replace(ServiceDescriptor.Singleton<ISearchParameterStatusManager, FailingSearchParameterStatusManager>());
        }
    }
}

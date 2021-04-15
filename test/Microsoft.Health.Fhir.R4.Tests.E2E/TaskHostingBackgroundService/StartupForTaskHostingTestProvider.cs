// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Health.Fhir.Core.Features.TaskManagement;
using Microsoft.Health.Fhir.Shared.Tests.E2E.Rest;

namespace Microsoft.Health.Fhir.Shared.Tests.E2E.TaskHostingBackgroundService
{
    public class StartupForTaskHostingTestProvider : StartupBaseForCustomProviders
    {
        public StartupForTaskHostingTestProvider(IConfiguration configuration)
            : base(configuration)
        {
        }

        public override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);

            var descriptor =
                new ServiceDescriptor(
                    typeof(ITaskFactory),
                    typeof(MockTaskFactory),
                    ServiceLifetime.Scoped);

            services.Replace(descriptor);
        }
    }
}

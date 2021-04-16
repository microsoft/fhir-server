// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.TaskManagement;

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

            // replace taskhosting configuration
            TaskHostingConfiguration configuration = new TaskHostingConfiguration()
            {
                Enabled = true,
                QueueId = "0",
                MaxRetryCount = 3,
            };

            IOptions<TaskHostingConfiguration> options = Options.Create(configuration);
            services.Replace(new ServiceDescriptor(typeof(IOptions<TaskHostingConfiguration>), options));

            // replace task factory with mock factory
            var descriptor =
                new ServiceDescriptor(
                    typeof(ITaskFactory),
                    typeof(MockTaskFactory),
                    ServiceLifetime.Scoped);

            services.Replace(descriptor);
        }
    }
}

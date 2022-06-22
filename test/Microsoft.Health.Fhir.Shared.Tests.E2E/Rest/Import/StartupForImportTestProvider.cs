// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Tests.E2E.Rest.Metric;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Import
{
    [RequiresIsolatedDatabase]
    public class StartupForImportTestProvider : StartupBaseForCustomProviders
    {
        public StartupForImportTestProvider(IConfiguration configuration)
            : base(configuration)
        {
        }

        public override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);

            services.Add<MetricHandler>()
                .Singleton()
                .AsService<INotificationHandler<ImportJobMetricsNotification>>();
        }
    }
}

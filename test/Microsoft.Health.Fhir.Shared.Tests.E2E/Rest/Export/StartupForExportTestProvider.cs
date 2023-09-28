// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Tests.E2E.Rest.Metric;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Export
{
    [RequiresIsolatedDatabase]
    public class StartupForExportTestProvider : StartupBaseForCustomProviders
    {
        public StartupForExportTestProvider(IConfiguration configuration)
            : base(configuration)
        {
        }

        public override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);

            FeatureConfiguration configuration = new FeatureConfiguration()
            {
                SupportsAnonymizedExport = true,
            };
            IOptions<FeatureConfiguration> options = Options.Create<FeatureConfiguration>(configuration);
            services.Replace(new ServiceDescriptor(typeof(IOptions<FeatureConfiguration>), options));

            services.Add<MetricHandler>()
                    .Singleton()
                    .AsService<INotificationHandler<ExportTaskMetricsNotification>>();
        }
    }
}

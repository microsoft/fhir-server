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
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Tests.E2E.Rest.Metric;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Export
{
    [RequiresIsolatedDatabase]
    public class StartupForExportTestProvider : StartupBaseForCustomProviders
    {
        private IConfiguration _configuration;

        public StartupForExportTestProvider(IConfiguration configuration)
            : base(configuration)
        {
            _configuration = configuration;
        }

        public override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);

            FeatureConfiguration featureConfiguration = new()
            {
                SupportsAnonymizedExport = true,
            };
            IOptions<FeatureConfiguration> featureOptions = Options.Create<FeatureConfiguration>(featureConfiguration);
            services.Replace(new ServiceDescriptor(typeof(IOptions<FeatureConfiguration>), featureOptions));

            ExportJobConfiguration existingExportOptions = new();
            _configuration.GetSection("FhirServer:Operations:Export").Bind(existingExportOptions);

            // ExportDataTestFixture generates 27 patients, 54 encounters, and, 108 observations with history / deletes.
            // We want to test the splitting of jobs and orchestration continuation. Hence this config.
            existingExportOptions.MaximumNumberOfResourcesPerQuery = 20;
            existingExportOptions.NumberOfParallelRecordRanges = 4;
            IOptions<ExportJobConfiguration> exportOptions = Options.Create<ExportJobConfiguration>(existingExportOptions);
            services.Replace(new ServiceDescriptor(typeof(IOptions<ExportJobConfiguration>), exportOptions));

            services.Add<MetricHandler>()
                    .Singleton()
                    .AsService<INotificationHandler<ExportTaskMetricsNotification>>();
        }
    }
}

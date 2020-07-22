// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Prometheus.SystemMetrics;

namespace Microsoft.Health.Fhir.Web
{
    public static class PrometheusMetricsServicesCollectionExtensions
    {
        public static void AddPrometheusMetrics(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            PrometheusMetricsConfig prometheusConfig = new PrometheusMetricsConfig();
            configuration.Bind("PrometheusMetrics", prometheusConfig);
            services.AddSingleton(Options.Create(prometheusConfig));

            if (prometheusConfig.Enabled)
            {
                services.Add<PrometheusMetricsServer>()
                    .Singleton()
                    .AsService<IStartable>();

                if (prometheusConfig.SystemMetrics)
                {
                    services.AddSystemMetrics();
                }
            }
        }
    }
}
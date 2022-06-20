// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Web;
using Prometheus;

namespace Microsoft.AspNetCore.Builder
{
    public static class PrometheusMetricsApplicationBuilderExtensions
    {
        public static IApplicationBuilder UsePrometheusHttpMetrics(this IApplicationBuilder app)
        {
            var prometheusMetricsConfig = app.ApplicationServices.GetService<IOptions<PrometheusMetricsConfig>>()?.Value;
            if (prometheusMetricsConfig?.Enabled == true)
            {
                if (prometheusMetricsConfig.HttpMetrics)
                {
                    app.UseHttpMetrics();
                }
            }

            return app;
        }
    }
}

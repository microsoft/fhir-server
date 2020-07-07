// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureKeyVault;
using Prometheus;
using Prometheus.DotNetRuntime;

namespace Microsoft.Health.Fhir.Web
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            KestrelMetricServer metricServer = null;

            var host = WebHost.CreateDefaultBuilder(args)
                .UseContentRoot(Path.GetDirectoryName(typeof(Program).Assembly.Location))
                .ConfigureAppConfiguration((hostContext, builder) =>
                {
                    var builtConfig = builder.Build();

                    var keyVaultEndpoint = builtConfig["KeyVault:Endpoint"];
                    if (!string.IsNullOrEmpty(keyVaultEndpoint))
                    {
                        var azureServiceTokenProvider = new AzureServiceTokenProvider();
                        var keyVaultClient = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(azureServiceTokenProvider.KeyVaultTokenCallback));
                        builder.AddAzureKeyVault(keyVaultEndpoint, keyVaultClient, new DefaultKeyVaultSecretManager());
                    }

                    builder.AddDevelopmentAuthEnvironmentIfConfigured(builtConfig);

                    if (bool.TryParse(builtConfig["PrometheusMetrics:enabled"], out bool prometheusEnabled) && prometheusEnabled)
                    {
                        int metricsPort = int.TryParse(builtConfig["PrometheusMetrics:port"], out metricsPort) ? metricsPort : 1234;
                        string metricsUrl = string.IsNullOrEmpty(builtConfig["PrometheusMetrics:url"]) ? "/metrics" : builtConfig["PrometheusMetrics:url"];

                        metricServer = new KestrelMetricServer(port: metricsPort, url: metricsUrl);

                        if (bool.TryParse(builtConfig["PrometheusMetrics:dotnetRuntimeMetrics"], out bool dotnetRuntimeMetrics) && dotnetRuntimeMetrics)
                        {
                            DotNetRuntimeStatsBuilder.Customize()
                                .WithThreadPoolSchedulingStats()
                                .WithContentionStats()
                                .WithGcStats()
                                .WithJitStats()
                                .WithThreadPoolStats()
                                .StartCollecting();
                        }

                        metricServer.Start();
                    }
                })
                .UseStartup<Startup>()
                .Build();

            host.Run();
        }
    }
}

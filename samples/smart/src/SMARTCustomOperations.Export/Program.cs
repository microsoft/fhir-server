// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using Microsoft.AzureHealth.DataServices.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SMARTCustomOperations.Export.Bindings;
using SMARTCustomOperations.Export.Configuration;
using SMARTCustomOperations.Export.Filters;

namespace SMARTCustomOperations.Export
{
    internal class Program
    {
        internal static async Task Main(string[] args)
        {
            ExportCustomOperationsConfig config = new ExportCustomOperationsConfig();
            using IHost host = new HostBuilder()
                .ConfigureAppConfiguration((context, configuration) =>
                {
                    configuration.Sources.Clear();
                    IHostEnvironment env = context.HostingEnvironment;

                    // Pull configuration from user secrets and local settings for local dev
                    // Pull from environment variables for Azure deployment
                    configuration
                        .AddUserSecrets(Assembly.GetExecutingAssembly(), true)
                        .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                        .AddEnvironmentVariables("AZURE_");

                    IConfigurationRoot configurationRoot = configuration.Build();
                    configurationRoot.Bind(config);

                    // config.Validate();
                })
                .ConfigureFunctionsWorkerDefaults()
                .ConfigureServices(services =>
                {
                    if (config.AppInsightsConnectionString is not null)
                    {
                        services.UseAppInsightsLogging(config.AppInsightsConnectionString, LogLevel.Information);
                        services.UseTelemetry(config.AppInsightsConnectionString);
                    }
                    else if (config.AppInsightsInstrumentationKey is not null)
                    {
                        services.UseAppInsightsLogging(config.AppInsightsInstrumentationKey, LogLevel.Information);
                        services.UseTelemetry(config.AppInsightsInstrumentationKey);
                    }

                    // Setup authenticator with DefaultAzureCredential for accessing the storage backend
                    services.UseAuthenticator();

                    services.UseAzureFunctionPipeline();

                    // First filter sets up the pipeline by extracting properties
                    services.AddInputFilter(typeof(ExtractPipelinePropertiesInputFilter));

                    // In the middle is our custom binding that will either hit the FHIR Service or Azure Storage
                    // Since we are using a custom binding, logic can be moved here instead of input filters.
                    services.AddBinding(typeof(ExportBinding));

                    // Next is the export operation check output filter to point export URLs to our APIM front end
                    services.AddOutputFilter(typeof(CheckExportJobOutputFilter));

                    // Finally the export operation output filter to change the content-location header
                    services.AddOutputFilter(typeof(ExportOperationOutputFilter));
                })
                .Build();

            await host.RunAsync();
        }
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using Microsoft.AzureHealth.DataServices.Clients.Headers;
using Microsoft.AzureHealth.DataServices.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SMARTCustomOperations.AzureAuth.Bindings;
using SMARTCustomOperations.AzureAuth.Configuration;
using SMARTCustomOperations.AzureAuth.Filters;
using SMARTCustomOperations.AzureAuth.Services;

namespace SMARTCustomOperations.AzureAuth
{
    internal class Program
    {
        internal static async Task Main(string[] args)
        {
            AzureAuthOperationsConfig config = new AzureAuthOperationsConfig();
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
                    config.Validate();
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

                    services.AddScoped<IAsymmetricAuthorizationService, AsymmetricAuthorizationService>();
                    services.AddScoped<IClientConfigService, KeyVaultClientConfiguratinService>();
                    services.AddScoped<GraphConsentService>();
                    services.AddHttpClient();

                    services.UseAzureFunctionPipeline();

                    services.UseCustomHeaders();
                    services.AddCustomHeader("Origin", "http://localhost", CustomHeaderType.RequestStatic);

                    services.AddSingleton<AzureAuthOperationsConfig>(config);

                    services.AddInputFilter(typeof(AuthorizeInputFilter));
                    services.AddInputFilter(typeof(TokenInputFilter));
                    services.AddInputFilter(typeof(ContextInfoInputFilter));
                    services.AddOutputFilter(typeof(TokenOutputFilter));

                    services.AddBinding<AzureActiveDirectoryBindingOptions>(typeof(AzureActiveDirectoryBinding), options =>
                    {
                        options.AzureActiveDirectoryEndpoint = "https://login.microsoftonline.com";
                    });

                    services.AddOutputFilter(typeof(TokenOutputFilter));
                })
                .Build();

            await host.RunAsync();
        }
    }
}

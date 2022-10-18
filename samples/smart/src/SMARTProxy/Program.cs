using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.AzureHealth.DataServices.Configuration;
using SMARTProxy.Configuration;
using Microsoft.Extensions.Logging;
using SMARTProxy.Filters;
using Microsoft.Extensions.DependencyInjection;
using SMARTProxy.Bindings;
using Microsoft.AzureHealth.DataServices.Clients.Headers;
using Microsoft.AzureHealth.DataServices.Bindings;

SMARTProxyConfig config = new SMARTProxyConfig();
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

        services.AddSingleton<SMARTProxyConfig>(config);

        services.UseAzureFunctionPipeline();

        services.UseCustomHeaders();
        services.AddCustomHeader("Origin", "http://localhost", CustomHeaderType.RequestStatic);

        services.AddInputFilter<SMARTProxyConfig>(typeof(AuthorizeInputFilter), options =>
        {
            options = config;
        });

        services.AddInputFilter<SMARTProxyConfig>(typeof(TokenInputFilter), options =>
        {
            options = config;
        });

        services.AddBinding<AzureActiveDirectoryBindingOptions>(typeof(AzureActiveDirectoryBinding), options =>
        {
            options.AzureActiveDirectoryEndpoint = "https://login.microsoftonline.com";
        });

        services.AddOutputFilter<SMARTProxyConfig>(typeof(TokenOutputFilter), options =>
        {
            options = config;
        });
    })
    .Build();

await host.RunAsync();

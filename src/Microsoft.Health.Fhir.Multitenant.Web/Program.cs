// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Health.Fhir.Api.OpenIddict.Extensions;
using Microsoft.Health.Fhir.Api.OpenIddict.FeatureProviders;
using Microsoft.Health.Fhir.Azure;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Multitenant.Core;
using Microsoft.Health.Fhir.Multitenant.Web;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer.Configs;

var builder = WebApplication.CreateBuilder(args);

// Load multitenant configuration
builder.Configuration.AddJsonFile("appsettings.multitenant.json", optional: false, reloadOnChange: true);

// Load the multitenant settings
var settings = builder.Configuration
    .GetSection(MultitenantSettings.SectionName)
    .Get<MultitenantSettings>() ?? new MultitenantSettings();

// Configure the router port
builder.WebHost.UseUrls($"http://localhost:{settings.RouterPort}");

// Determine if running in development mode
bool isDevelopment = builder.Environment.IsDevelopment() ||
    string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase);

// Register services for the router
var httpClientBuilder = builder.Services.AddHttpClient("RouterClient");
if (isDevelopment)
{
    // Only bypass certificate validation in development
    httpClientBuilder.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
    });
}

// Register the tenant manager
builder.Services.AddSingleton<ITenantManager>(new TenantManager(settings));

// Register the tenant host service with FHIR server configuration
builder.Services.AddSingleton<TenantHostService>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<TenantHostService>>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

    return new TenantHostService(
        configuration,
        logger,
        loggerFactory,
        (tenantBuilder, tenantConfig) => ConfigureTenantBuilder(tenantBuilder, tenantConfig, isDevelopment),
        ConfigureTenantApp);
});

builder.Services.AddHostedService(sp => sp.GetRequiredService<TenantHostService>());

var app = builder.Build();

// Configure the router middleware
app.UseMiddleware<RouterMiddleware>();

Console.WriteLine($"Starting multitenant FHIR server router on port {settings.RouterPort}");
Console.WriteLine($"Configured tenants: {string.Join(", ", settings.Tenants.Select(t => $"{t.TenantId}:{t.Port}"))}");

await app.RunAsync();

// Configure the WebApplicationBuilder for each tenant FHIR server instance
static void ConfigureTenantBuilder(WebApplicationBuilder tenantBuilder, TenantConfiguration tenantConfig, bool isDevelopment)
{
    // Load base FHIR server configuration
    tenantBuilder.Configuration.AddJsonFile("appsettings.json", optional: true);

    var services = tenantBuilder.Services;
    var configuration = tenantBuilder.Configuration;

    // Configure FHIR server
    var fhirServerBuilder = services.AddFhirServer(
        configuration,
        fhirServerConfiguration =>
        {
            fhirServerConfiguration.Security.AddAuthenticationLibrary = (svc, sec) => AddAuthenticationLibrary(svc, sec, isDevelopment);
        },
        mvcBuilderAction: mvcBuilder =>
        {
            mvcBuilder.PartManager.FeatureProviders.Remove(
                mvcBuilder.PartManager.FeatureProviders.OfType<ControllerFeatureProvider>().FirstOrDefault());
            mvcBuilder.PartManager.FeatureProviders.Add(new FhirControllerFeatureProvider(configuration));
        })
        .AddAzureExportDestinationClient()
        .AddAzureExportClientInitializer(configuration)
        .AddContainerRegistryTokenProvider()
        .AddContainerRegistryAccessValidator()
        .AddAzureIntegrationDataStoreClient(configuration)
        .AddConvertData()
        .AddMemberMatch();

    // Add development identity provider for testing
    services.AddDevelopmentIdentityProvider(configuration);

    // Configure runtime and data store
    string dataStore = configuration["DataStore"] ?? "SqlServer";
    IFhirRuntimeConfiguration runtimeConfiguration;

    if (KnownDataStores.IsCosmosDbDataStore(dataStore))
    {
        runtimeConfiguration = new AzureApiForFhirRuntimeConfiguration();
        fhirServerBuilder.AddCosmosDb();
    }
    else if (KnownDataStores.IsSqlServerDataStore(dataStore))
    {
        runtimeConfiguration = new AzureHealthDataServicesRuntimeConfiguration();
        fhirServerBuilder.AddSqlServer(config =>
        {
            configuration.GetSection(SqlServerDataStoreConfiguration.SectionName).Bind(config);
        });
        services.Configure<SqlRetryServiceOptions>(configuration.GetSection(SqlRetryServiceOptions.SqlServer));
    }
    else
    {
        throw new InvalidOperationException($"Invalid data store type '{dataStore}'.");
    }

    services.AddSingleton<IFhirRuntimeConfiguration>(runtimeConfiguration);

    // Set up Bundle Orchestrator
    fhirServerBuilder.AddBundleOrchestrator(configuration);
}

// Configure the WebApplication for each tenant FHIR server instance
static void ConfigureTenantApp(WebApplication tenantApp, TenantConfiguration tenantConfig)
{
    // Use FHIR server middleware pipeline
    tenantApp.UseFhirServer(
        DevelopmentIdentityProviderRegistrationExtensions.UseDevelopmentIdentityProviderIfConfigured);
}

// Add JWT Bearer authentication
static void AddAuthenticationLibrary(IServiceCollection services, SecurityConfiguration securityConfiguration, bool isDevelopment)
{
    services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.Authority = securityConfiguration.Authentication.Authority;
        options.Audience = securityConfiguration.Authentication.Audience;
        options.TokenValidationParameters.RoleClaimType = securityConfiguration.Authorization.RolesClaim;
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = !isDevelopment; // Only disable HTTPS metadata in development
        options.Challenge = $"Bearer authorization_uri=\"{securityConfiguration.Authentication.Authority}\", resource_id=\"{securityConfiguration.Authentication.Audience}\", realm=\"{securityConfiguration.Authentication.Audience}\"";
    });
}

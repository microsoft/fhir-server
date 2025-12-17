// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Builder;
using Microsoft.Health.Fhir.Multitenant.Core;

namespace Microsoft.Health.Fhir.Multitenant.Web;

/// <summary>
/// Background service that manages the lifecycle of tenant FHIR server instances.
/// </summary>
public class TenantHostService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<TenantHostService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Action<WebApplicationBuilder, TenantConfiguration> _configureBuilder;
    private readonly Action<WebApplication, TenantConfiguration> _configureApp;
    private readonly List<TenantHost> _tenantHosts = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantHostService"/> class.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="configureBuilder">Action to configure the WebApplicationBuilder for each tenant.</param>
    /// <param name="configureApp">Action to configure the WebApplication for each tenant.</param>
    public TenantHostService(
        IConfiguration configuration,
        ILogger<TenantHostService> logger,
        ILoggerFactory loggerFactory,
        Action<WebApplicationBuilder, TenantConfiguration> configureBuilder,
        Action<WebApplication, TenantConfiguration> configureApp)
    {
        _configuration = configuration;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _configureBuilder = configureBuilder;
        _configureApp = configureApp;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = _configuration
            .GetSection(MultitenantSettings.SectionName)
            .Get<MultitenantSettings>();

        if (settings?.Tenants == null || settings.Tenants.Count == 0)
        {
            _logger.LogError("No tenant configuration found in {Section} section", MultitenantSettings.SectionName);
            return;
        }

        _logger.LogInformation("Starting {Count} tenant instances", settings.Tenants.Count);

        // Start all tenant instances
        foreach (var tenantConfig in settings.Tenants)
        {
            try
            {
                var host = new TenantHost(
                    tenantConfig,
                    _loggerFactory.CreateLogger<TenantHost>(),
                    _configureBuilder,
                    _configureApp);

                await host.StartAsync(stoppingToken);
                _tenantHosts.Add(host);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to start tenant {TenantId} on port {Port}",
                    tenantConfig.TenantId,
                    tenantConfig.Port);
            }
        }

        _logger.LogInformation(
            "Successfully started {Count} out of {Total} tenant instances",
            _tenantHosts.Count,
            settings.Tenants.Count);

        // Keep running until cancellation
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Tenant host service received shutdown signal");
        }
    }

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping {Count} tenant instances", _tenantHosts.Count);

        foreach (var host in _tenantHosts)
        {
            try
            {
                await host.StopAsync(cancellationToken);
                await host.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error stopping tenant {TenantId}",
                    host.Configuration.TenantId);
            }
        }

        _tenantHosts.Clear();
        await base.StopAsync(cancellationToken);
    }
}

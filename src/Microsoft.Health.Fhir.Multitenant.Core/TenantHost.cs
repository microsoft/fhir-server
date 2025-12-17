// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Multitenant.Core;

/// <summary>
/// Represents a tenant-specific FHIR server host that wraps a WebApplication instance.
/// </summary>
public class TenantHost : IAsyncDisposable
{
    private readonly TenantConfiguration _config;
    private readonly ILogger<TenantHost> _logger;
    private readonly Action<WebApplicationBuilder, TenantConfiguration> _configureBuilder;
    private readonly Action<WebApplication, TenantConfiguration> _configureApp;
    private WebApplication? _app;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantHost"/> class.
    /// </summary>
    /// <param name="config">The tenant configuration.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="configureBuilder">Action to configure the WebApplicationBuilder.</param>
    /// <param name="configureApp">Action to configure the WebApplication.</param>
    public TenantHost(
        TenantConfiguration config,
        ILogger<TenantHost> logger,
        Action<WebApplicationBuilder, TenantConfiguration> configureBuilder,
        Action<WebApplication, TenantConfiguration> configureApp)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(configureBuilder);
        ArgumentNullException.ThrowIfNull(configureApp);

        _config = config;
        _logger = logger;
        _configureBuilder = configureBuilder;
        _configureApp = configureApp;
    }

    /// <summary>
    /// Gets the tenant configuration.
    /// </summary>
    public TenantConfiguration Configuration => _config;

    /// <summary>
    /// Gets a value indicating whether the host is running.
    /// </summary>
    public bool IsRunning => _app != null;

    /// <summary>
    /// Starts the tenant FHIR server instance.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Starting tenant {TenantId} on port {Port}",
            _config.TenantId,
            _config.Port);

        var builder = WebApplication.CreateBuilder();

        // Configure this tenant instance to listen on its specific port
        builder.WebHost.UseUrls($"http://localhost:{_config.Port}");

        // Add tenant-specific configuration
        var tenantSettings = new Dictionary<string, string?>
        {
            ["FhirServer:TenantId"] = _config.TenantId,
        };

        // Add connection string if provided
        if (!string.IsNullOrWhiteSpace(_config.ConnectionString))
        {
            tenantSettings["SqlServer:ConnectionString"] = _config.ConnectionString;
        }

        // Add any additional tenant-specific settings
        foreach (var setting in _config.Settings)
        {
            tenantSettings[setting.Key] = setting.Value;
        }

        builder.Configuration.AddInMemoryCollection(tenantSettings);

        // Allow the consumer to configure the builder (e.g., add FHIR services)
        _configureBuilder(builder, _config);

        _app = builder.Build();

        // Allow the consumer to configure the app (e.g., add FHIR middleware)
        _configureApp(_app, _config);

        await _app.StartAsync(cancellationToken);

        _logger.LogInformation(
            "Tenant {TenantId} started successfully on port {Port}",
            _config.TenantId,
            _config.Port);
    }

    /// <summary>
    /// Stops the tenant FHIR server instance.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_app != null)
        {
            _logger.LogInformation(
                "Stopping tenant {TenantId}",
                _config.TenantId);

            await _app.StopAsync(cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_app != null)
        {
            await _app.DisposeAsync();
            _app = null;
        }

        GC.SuppressFinalize(this);
    }
}

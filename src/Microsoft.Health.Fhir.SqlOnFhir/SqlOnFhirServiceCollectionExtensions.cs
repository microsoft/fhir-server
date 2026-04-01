// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DeltaLake.Interfaces;
using DeltaLake.Table;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.SqlOnFhir.Channels;
using Microsoft.Health.Fhir.SqlOnFhir.Materialization;
using Microsoft.Health.Fhir.SqlOnFhir.Materialization.Jobs;
using Microsoft.Health.Fhir.Subscriptions.Channels;
using Microsoft.Health.Fhir.Subscriptions.Models;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.SqlOnFhir;

/// <summary>
/// Extension methods for registering SQL on FHIR services with dependency injection.
/// </summary>
public static class SqlOnFhirServiceCollectionExtensions
{
    /// <summary>
    /// Adds SQL on FHIR ViewDefinition evaluation services to the service collection.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSqlOnFhir(this IServiceCollection services)
    {
        services.AddSingleton<IViewDefinitionEvaluator, ViewDefinitionEvaluator>();
        services.AddSingleton<IViewDefinitionSchemaManager, SqlServerViewDefinitionSchemaManager>();
        services.AddSingleton<IViewDefinitionMaterializer, SqlServerViewDefinitionMaterializer>();
        services.AddSingleton<SqlServerViewDefinitionMaterializer>();
        services.AddSingleton<IViewDefinitionSubscriptionManager, ViewDefinitionSubscriptionManager>();

        // Register MediatR handlers from this assembly (not auto-discovered by KnownAssemblies).
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<ViewDefinitionEvaluator>());

        // Register Delta Lake engine and materializer for Fabric target.
        // The engine is a long-lived resource that manages the FFI bridge to delta-rs.
        services.AddSingleton<IEngine>(_ => new DeltaEngine(EngineOptions.Default));
        services.AddSingleton<DeltaLakeViewDefinitionMaterializer>();
        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IOptions<SqlOnFhirMaterializationConfiguration>>();
            var sqlMaterializer = sp.GetRequiredService<SqlServerViewDefinitionMaterializer>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MaterializerFactory>>();

            // Resolve optional materializers based on configuration
            ParquetViewDefinitionMaterializer? parquetMaterializer = config.Value.IsStorageConfigured
                ? sp.GetService<ParquetViewDefinitionMaterializer>()
                : null;

            DeltaLakeViewDefinitionMaterializer? deltaLakeMaterializer = config.Value.IsStorageConfigured
                ? sp.GetRequiredService<DeltaLakeViewDefinitionMaterializer>()
                : null;

            return new MaterializerFactory(
                sqlMaterializer,
                config,
                logger,
                parquetMaterializer: parquetMaterializer,
                deltaLakeMaterializer: deltaLakeMaterializer);
        });

        // Register background jobs for ViewDefinition population.
        // Uses the same auto-discovery pattern as the Subscriptions module.
        IEnumerable<TypeRegistrationBuilder> jobs = services
            .TypesInSameAssemblyAs<ViewDefinitionPopulationOrchestratorJob>()
            .AssignableTo<IJob>()
            .Transient()
            .AsSelf();

        foreach (TypeRegistrationBuilder job in jobs)
        {
            job.AsDelegate<Func<IJob>>();
        }

        // Register the ViewDefinition refresh subscription channel.
        services.AddTransient<ViewDefinitionRefreshChannel>();
        services.AddTransient<ISubscriptionChannel, ViewDefinitionRefreshChannel>();

        // Register cleanup behavior: drops materialized SQL tables when Library/ViewDef is deleted.
        services.AddTransient<
            MediatR.IPipelineBehavior<
                Microsoft.Health.Fhir.Core.Messages.Delete.DeleteResourceRequest,
                Microsoft.Health.Fhir.Core.Messages.Delete.DeleteResourceResponse>,
            ViewDefinitionLibraryCleanupBehavior>();

        // Register creation behavior: triggers materialization when Library/ViewDef is POSTed.
        services.AddTransient<
            MediatR.IPipelineBehavior<
                Microsoft.Health.Fhir.Core.Messages.Create.CreateResourceRequest,
                Microsoft.Health.Fhir.Core.Messages.Upsert.UpsertResourceResponse>,
            ViewDefinitionLibraryRegistrationBehavior>();

        // Register startup recovery and multi-node sync service for ViewDefinition Library resources.
        // Waits for SearchParametersInitializedNotification, then polls every 10s for changes.
        services.AddSingleton<ViewDefinitionSyncService>();
        services.AddHostedService(sp => sp.GetRequiredService<ViewDefinitionSyncService>());
        services.AddSingleton<INotificationHandler<SearchParametersInitializedNotification>>(
            sp => sp.GetRequiredService<ViewDefinitionSyncService>());

        return services;
    }

    /// <summary>
    /// Registers the ViewDefinition refresh channel with the subscription channel factory.
    /// Call this after the subscription infrastructure has been initialized.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <returns>The service provider for chaining.</returns>
    public static IServiceProvider UseSqlOnFhirChannels(this IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetService<Microsoft.Extensions.Logging.ILoggerFactory>();
        var logger = loggerFactory?.CreateLogger("Microsoft.Health.Fhir.SqlOnFhir")!;

        var factory = serviceProvider.GetService<SubscriptionChannelFactory>();
        if (factory != null)
        {
            factory.RegisterExternalChannel(SubscriptionChannelType.ViewDefinitionRefresh, typeof(ViewDefinitionRefreshChannel));
            Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(logger, "Registered ViewDefinitionRefreshChannel with subscription channel factory");
        }
        else
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(logger, "SubscriptionChannelFactory not found — ViewDefinition refresh channel NOT registered");
        }

        return serviceProvider;
    }
}

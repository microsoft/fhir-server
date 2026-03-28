// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.SqlOnFhir.Materialization;
using Microsoft.Health.Fhir.SqlOnFhir.Materialization.Jobs;
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

        return services;
    }
}

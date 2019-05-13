// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using EnsureThat;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.SqlServer.Api.Controllers;
using Microsoft.Health.Fhir.SqlServer.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Health;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class FhirServerBuilderSqlServerRegistrationExtensions
    {
        public static IServiceCollection AddExperimentalSqlServer(this IServiceCollection serviceCollection, Action<SqlServerDataStoreConfiguration> configureAction = null)
        {
            EnsureArg.IsNotNull(serviceCollection, nameof(serviceCollection));

            serviceCollection.Add(provider =>
                {
                    var config = new SqlServerDataStoreConfiguration();
                    provider.GetService<IConfiguration>().GetSection("SqlServer").Bind(config);
                    configureAction?.Invoke(config);

                    if (string.IsNullOrWhiteSpace(config.ConnectionString))
                    {
                        config.ConnectionString = LocalDatabase.DefaultConnectionString;
                    }

                    return config;
                })
                .Singleton()
                .AsSelf();

            serviceCollection.Add<SchemaUpgradeRunner>()
                .Singleton()
                .AsSelf();

            serviceCollection.Add<SchemaInformation>()
                .Singleton()
                .AsSelf();

            serviceCollection.Add<SchemaInitializer>()
                .Singleton()
                .AsService<IStartable>();

            serviceCollection.Add<SqlServerFhirModel>()
                .Singleton()
                .AsSelf();

            serviceCollection.Add<SqlServerFhirDataStore>()
                .Singleton()
                .AsSelf()
                .AsImplementedInterfaces();

            serviceCollection.Add<SqlServerSearchService>()
                .Singleton()
                .AsSelf()
                .AsImplementedInterfaces();

            serviceCollection
                .AddHealthChecks()
                .AddCheck<SqlServerHealthCheck>(nameof(SqlServerHealthCheck));

            // This is only needed while adding in the ConfigureServices call in the E2E TestServer scenario
            // During normal usage, the controller should be automatically discovered.
            serviceCollection.AddMvc().AddApplicationPart(typeof(SchemaController).Assembly);

            AddSqlServerTableRowParameterGenerators(serviceCollection);

            return serviceCollection;
        }

        internal static void AddSqlServerTableRowParameterGenerators(this IServiceCollection serviceCollection)
        {
            foreach (var type in typeof(SqlServerFhirDataStore).Assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract))
            {
                foreach (var interfaceType in type.GetInterfaces())
                {
                    if (interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof(IStoredProcedureTableValuedParametersGenerator<,>))
                    {
                        serviceCollection.AddSingleton(type);
                    }

                    if (interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof(ITableValuedParameterRowGenerator<,>))
                    {
                        serviceCollection.AddSingleton(interfaceType, type);
                    }
                }
            }
        }
    }
}

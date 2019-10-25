// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using EnsureThat;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.SqlServer.Api.Controllers;
using Microsoft.Health.Fhir.SqlServer.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Health;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class FhirServerBuilderSqlServerRegistrationExtensions
    {
        public static IFhirServerBuilder AddExperimentalSqlServer(this IFhirServerBuilder fhirServerBuilder, Action<SqlServerDataStoreConfiguration> configureAction = null)
        {
            EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));
            IServiceCollection services = fhirServerBuilder.Services;

            services.Add(provider =>
                {
                    var config = new SqlServerDataStoreConfiguration();
                    provider.GetService<IConfiguration>().GetSection("SqlServer").Bind(config);
                    configureAction?.Invoke(config);

                    return config;
                })
                .Singleton()
                .AsSelf();

            services.Add<SchemaUpgradeRunner>()
                .Singleton()
                .AsSelf();

            services.Add<SchemaInformation>()
                .Singleton()
                .AsSelf();

            services.Add<SchemaInitializer>()
                .Singleton()
                .AsService<IStartable>();

            services.Add<SqlServerFhirModel>()
                .Singleton()
                .AsSelf();

            services.Add<SearchParameterToSearchValueTypeMap>()
                .Singleton()
                .AsSelf();

            services.Add<SqlServerFhirDataStore>()
                .Scoped()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<SqlServerFhirOperationDataStore>()
                .Scoped()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<SqlServerSearchService>()
                .Scoped()
                .AsSelf()
                .AsImplementedInterfaces();

            services
                .AddHealthChecks()
                .AddCheck<SqlServerHealthCheck>(nameof(SqlServerHealthCheck));

            // This is only needed while adding in the ConfigureServices call in the E2E TestServer scenario
            // During normal usage, the controller should be automatically discovered.
            services.AddMvc().AddApplicationPart(typeof(SchemaController).Assembly);

            AddSqlServerTableRowParameterGenerators(services);

            services.Add<NormalizedSearchParameterQueryGeneratorFactory>()
                .Singleton()
                .AsSelf();

            services.Add<SqlRootExpressionRewriter>()
                .Singleton()
                .AsSelf();

            services.Add<ChainFlatteningRewriter>()
                .Singleton()
                .AsSelf();

            services.Add<StringOverflowRewriter>()
                .Singleton()
                .AsSelf();

            return fhirServerBuilder;
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
                        serviceCollection.Add(type).Singleton().AsSelf().AsService(interfaceType);
                    }
                }
            }
        }
    }
}

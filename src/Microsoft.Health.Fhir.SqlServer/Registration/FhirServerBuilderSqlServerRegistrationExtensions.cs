// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.Registry;
using Microsoft.Health.SqlServer.Api.Registration;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer.Registration;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class FhirServerBuilderSqlServerRegistrationExtensions
    {
        public static IFhirServerBuilder AddSqlServer(this IFhirServerBuilder fhirServerBuilder, Action<SqlServerDataStoreConfiguration> configureAction = null)
        {
            EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));
            IServiceCollection services = fhirServerBuilder.Services;

            services.AddSqlServerBase<SchemaVersion>(configureAction);
            services.AddSqlServerApi();

            services.Add(provider => new SchemaInformation((int)SchemaVersion.V1, (int)SchemaVersion.V3))
                .Singleton()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<SqlServerStatusRegistryDataStore>()
                .Scoped()
                .AsSelf()
                .AsImplementedInterfaces()
                .AsFactory<IScoped<ISearchParameterRegistryDataStore>>()
                .AsFactory<IScoped<SqlServerStatusRegistryDataStore>>();

            services.Add<SqlTransactionHandler>()
                .Singleton()
                .AsSelf()
                .AsImplementedInterfaces();

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

            AddSqlServerTableRowParameterGenerators(services);

            services.Add<SqlServerStatusRegistryInitializer>()
                .Transient()
                .AsImplementedInterfaces();

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

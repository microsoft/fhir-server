// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using EnsureThat;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.SqlServer.Features.Operations.Reindex;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.Registry;
using Microsoft.Health.SqlServer.Api.Registration;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Registration;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class FhirServerBuilderSqlServerRegistrationExtensions
    {
        public static IFhirServerBuilder AddSqlServer(this IFhirServerBuilder fhirServerBuilder, IConfiguration configuration, Action<SqlServerDataStoreConfiguration> configureAction = null)
        {
            EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));
            IServiceCollection services = fhirServerBuilder.Services;

            services.AddSqlServerBase<SchemaVersion>(configuration, configureAction);
            services.AddSqlServerApi();

            services.Add(provider => new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max))
                .Singleton()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<SqlServerSearchParameterStatusDataStore>()
                .Singleton()
                .AsSelf()
                .ReplaceService<ISearchParameterStatusDataStore>();

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

            services.Add<SearchParamTableExpressionQueryGeneratorFactory>()
                .Singleton()
                .AsSelf();

            services.Add<SqlRootExpressionRewriter>()
                .Singleton()
                .AsSelf();

            services.Add<ChainFlatteningRewriter>()
                .Singleton()
                .AsSelf();

            services.Add<SortRewriter>()
                .Singleton()
                .AsSelf();

            services.Add<PartitionEliminationRewriter>()
                .Singleton()
                .AsSelf();

            services.Add<SqlServerSortingValidator>()
                .Singleton()
                .AsSelf()
                .AsImplementedInterfaces();

            services.AddFactory<IScoped<SqlConnectionWrapperFactory>>();

            services.Add<SqlServerFhirModel>()
                .Singleton()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<SchemaUpgradedHandler>()
                .Transient()
                .AsImplementedInterfaces();

            services.Add<SqlServerSearchParameterValidator>()
                .Singleton()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<ReindexJobSqlThrottlingController>()
                .Singleton()
                .AsImplementedInterfaces();

            return fhirServerBuilder;
        }

        internal static void AddSqlServerTableRowParameterGenerators(this IServiceCollection serviceCollection)
        {
            var types = typeof(SqlServerFhirDataStore).Assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract).ToArray();
            foreach (var type in types)
            {
                var interfaces = type.GetInterfaces().ToArray();

                foreach (var interfaceType in interfaces)
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

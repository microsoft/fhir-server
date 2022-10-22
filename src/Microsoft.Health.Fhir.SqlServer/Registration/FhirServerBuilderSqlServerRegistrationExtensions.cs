// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using EnsureThat;
using MediatR.Pipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.SqlServer.Features.Operations;
using Microsoft.Health.Fhir.SqlServer.Features.Operations.Import;
using Microsoft.Health.Fhir.SqlServer.Features.Operations.Import.DataGenerator;
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
        public static IFhirServerBuilder AddSqlServer(this IFhirServerBuilder fhirServerBuilder, Action<SqlServerDataStoreConfiguration> configureAction = null)
        {
            EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));
            IServiceCollection services = fhirServerBuilder.Services;

            services.AddSqlServerConnection(configureAction);
            services.AddSqlServerManagement<SchemaVersion>();
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
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<SqlQueueClient>()
                .Scoped()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<SqlImportOperation>()
                .Scoped()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<SqlResourceBulkImporter>()
                .Transient()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<SqlResourceMetaPopulator>()
                .Transient()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<CompressedRawResourceConverter>()
                .Transient()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<SqlBulkCopyDataWrapperFactory>()
                .Transient()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<DateTimeSearchParamsTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<NumberSearchParamsTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<QuantitySearchParamsTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<ReferenceSearchParamsTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<ReferenceTokenCompositeSearchParamsTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<StringSearchParamsTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<TokenDateTimeCompositeSearchParamsTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<TokenNumberNumberCompositeSearchParamsTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<TokenQuantityCompositeSearchParamsTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<TokenSearchParamsTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<TokenStringCompositeSearchParamsTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<TokenTextSearchParamsTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<TokenTokenCompositeSearchParamsTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<UriSearchParamsTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<ResourceWriteClaimTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<CompartmentAssignmentTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<SqlStoreSequenceIdGenerator>()
                .Transient()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<PurgeOperationCapabilityProvider>()
                .Transient()
                .AsImplementedInterfaces();

            services.Add(typeof(SqlExceptionActionProcessor<,>))
                .Transient()
                .AsSelf()
                .AsService(typeof(IRequestExceptionAction<,>));

            services.Add<CompartmentSearchRewriter>()
                .Singleton()
                .AsSelf();

            services.Add<DefragWorker>()
                .Singleton()
                .AsSelf();

            services.AddHostedService<DefragBackgroundService>();

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

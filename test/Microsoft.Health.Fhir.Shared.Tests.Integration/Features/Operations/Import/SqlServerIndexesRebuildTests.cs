// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Shared.Tests.Integration.Features.Operations.Import;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.JobManagement;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Schema.Manager;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.Test.Utilities;
using Microsoft.SqlServer.Dac.Compare;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Operations.Import
{
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
    public class SqlServerIndexesRebuildTests : IClassFixture<SqlServerFhirStorageTestsFixture>
    {
        private const string LocalConnectionString = "server=(local);Integrated Security=true;TrustServerCertificate=True";
        private const string MasterDatabaseName = "master";
        private const string PrototypeType = "PROTOTYPE";
        private const string RebuildType = "REBUILD";

        public SqlServerIndexesRebuildTests()
        {
        }

        [Fact]
        public async Task GivenTwoDatabases_WhenOneDisableAndRebuildIndexes_ThenTwoDatabasesShouldBeTheSame()
        {
            await VerifyDatabasesStatus(false);
        }

        [Fact]
        public async Task GivenImportOperationEnabled_WhenRunRebuildCommandsCrash_ThenOperationShouldBeRestartAndCompleted()
        {
            await VerifyDatabasesStatus(true);
        }

        private async Task<(SqlImportOperation sqlImportOperation, List<(string tableName, string columns, long startSurrogatedId)> tables, SqlConnectionWrapperFactory sqlConnectionWrapperFactory, SqlServerFhirStorageTestHelper helper)> InitializeDatabaseAndOperation(string databaseName, long startSurrogateId)
        {
            (var helper, var sqlConnectionWrapperFactory, var store, var sqlServerFhirModel, var schemaInformation) = await SetupTestHelperAndCreateDatabase(databaseName, SchemaVersionConstants.Max);

            var operationsConfiguration = Substitute.For<IOptions<OperationsConfiguration>>();
            operationsConfiguration.Value.Returns(new OperationsConfiguration()
            {
                Import = new ImportTaskConfiguration()
                {
                    DisableOptionalIndexesForImport = true,
                },
            });

            var sqlImportOperation = new SqlImportOperation(sqlConnectionWrapperFactory, store, sqlServerFhirModel, operationsConfiguration, schemaInformation, NullLogger<SqlImportOperation>.Instance);

            var tables = new List<(string tableName, string columns, long startSurrogatedId)>();

            tables.Add(await ImportDataAsync(sqlImportOperation, startSurrogateId, 10, 103, TestBulkDataProvider.GenerateStringSearchParamsTable));
            tables.Add(await ImportDataAsync(sqlImportOperation, startSurrogateId, 10, 103, TestBulkDataProvider.GenerateCompartmentAssignmentTable));
            tables.Add(await ImportDataAsync(sqlImportOperation, startSurrogateId, 10, 103, TestBulkDataProvider.GenerateDateTimeSearchParamsTable));
            tables.Add(await ImportDataAsync(sqlImportOperation, startSurrogateId, 10, 103, TestBulkDataProvider.GenerateNumberSearchParamsTable));
            tables.Add(await ImportDataAsync(sqlImportOperation, startSurrogateId, 10, 103, TestBulkDataProvider.GenerateQuantitySearchParamsTable));
            tables.Add(await ImportDataAsync(sqlImportOperation, startSurrogateId, 10, 103, TestBulkDataProvider.GenerateReferenceSearchParamsTable));
            tables.Add(await ImportDataAsync(sqlImportOperation, startSurrogateId, 10, 103, TestBulkDataProvider.GenerateReferenceTokenCompositeSearchParamsTable));
            tables.Add(await ImportDataAsync(sqlImportOperation, startSurrogateId, 10, 103, TestBulkDataProvider.GenerateResourceTable));
            tables.Add(await ImportDataAsync(sqlImportOperation, startSurrogateId, 10, 103, TestBulkDataProvider.GenerateResourceWriteClaimTable));
            tables.Add(await ImportDataAsync(sqlImportOperation, startSurrogateId, 10, 103, TestBulkDataProvider.GenerateTokenDateTimeCompositeSearchParamsTable));
            tables.Add(await ImportDataAsync(sqlImportOperation, startSurrogateId, 10, 103, TestBulkDataProvider.GenerateTokenNumberNumberCompositeSearchParamsTable));
            tables.Add(await ImportDataAsync(sqlImportOperation, startSurrogateId, 10, 103, TestBulkDataProvider.GenerateTokenQuantityCompositeSearchParamsTable));
            tables.Add(await ImportDataAsync(sqlImportOperation, startSurrogateId, 10, 103, TestBulkDataProvider.GenerateTokenSearchParamsTable));
            tables.Add(await ImportDataAsync(sqlImportOperation, startSurrogateId, 10, 103, TestBulkDataProvider.GenerateTokenStringCompositeSearchParamsTable));
            tables.Add(await ImportDataAsync(sqlImportOperation, startSurrogateId, 10, 103, TestBulkDataProvider.GenerateTokenTextSearchParamsTable));
            tables.Add(await ImportDataAsync(sqlImportOperation, startSurrogateId, 10, 103, TestBulkDataProvider.GenerateTokenTokenCompositeSearchParamsTable));
            tables.Add(await ImportDataAsync(sqlImportOperation, startSurrogateId, 10, 103, TestBulkDataProvider.GenerateUriSearchParamsTable));

            return (sqlImportOperation, tables, sqlConnectionWrapperFactory, helper);
        }

        private async Task VerifyDatabasesStatus(bool crash)
        {
            long startSurrogateId = ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(DateTime.Now);
            var prototypeDatabaseName = $"{PrototypeType}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{BigInteger.Abs(new BigInteger(Guid.NewGuid().ToByteArray()))}";
            var rebuildDatabaseName = $"{RebuildType}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{BigInteger.Abs(new BigInteger(Guid.NewGuid().ToByteArray()))}";

            (var prototypeSqlImportOperation, var prototypeTables, var prototypeSqlConnectionWrapperFactory, var prototypeHelper) = await InitializeDatabaseAndOperation(prototypeDatabaseName, startSurrogateId);
            (var rebuildSqlImportOperation, var rebuildTables, var rebuildSqlConnectionWrapperFactory, var rebuildHelper) = await InitializeDatabaseAndOperation(rebuildDatabaseName, startSurrogateId);

            // Disable indexes
            await rebuildSqlImportOperation.PreprocessAsync(CancellationToken.None);

            if (crash)
            {
                CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
                cancellationTokenSource.CancelAfter(5000);
                Exception exception = await Assert.ThrowsAnyAsync<Exception>(() => rebuildSqlImportOperation.PostprocessAsync(cancellationTokenSource.Token));

                // Check exception is RetriableJobException or TaskCanceledException
                Assert.True(exception is RetriableJobException || exception is TaskCanceledException);
            }

            // Rebuild Indexes
            await rebuildSqlImportOperation.PostprocessAsync(CancellationToken.None);

            var diff = await CompareDatabaseSchemas(prototypeDatabaseName, rebuildDatabaseName);
            Assert.Empty(diff);
            foreach (var tableInfo in prototypeTables)
            {
                await CheckTableDataAsync(tableInfo.tableName, tableInfo.columns, prototypeSqlConnectionWrapperFactory, rebuildSqlConnectionWrapperFactory, startSurrogateId, startSurrogateId + 10);
            }

            await prototypeHelper.DeleteDatabase(prototypeDatabaseName);
            await rebuildHelper.DeleteDatabase(rebuildDatabaseName);
        }

        private async Task<(string tableName, string columns, long startSurrogatedId)> ImportDataAsync(SqlImportOperation sqlImportOperation, long startSurrogateId, int count, short resourceTypeId, Func<int, long, short, string, DataTable> tableGenerator, string resourceId = null)
        {
            DataTable inputTable = tableGenerator(count, startSurrogateId, resourceTypeId, resourceId);
            await sqlImportOperation.BulkCopyDataAsync(inputTable, CancellationToken.None);
            DataColumn[] columns = new DataColumn[inputTable.Columns.Count];
            inputTable.Columns.CopyTo(columns, 0);
            string columnsString = string.Join(',', columns.Select(c => c.ColumnName));
            return (inputTable.TableName, columnsString, startSurrogateId);
        }

        private async Task<(SqlServerFhirStorageTestHelper testHelper, SqlConnectionWrapperFactory sqlConnectionWrapperFactory, SqlServerFhirDataStore store, SqlServerFhirModel sqlServerFhirModel, SchemaInformation schemaInformation)> SetupTestHelperAndCreateDatabase(string databaseName, int maxSchemaVersion)
        {
            var initialConnectionString = Environment.GetEnvironmentVariable("SqlServer:ConnectionString") ?? LocalConnectionString;

            var searchService = Substitute.For<ISearchService>();
            ISearchParameterDefinitionManager defManager = new SearchParameterDefinitionManager(ModelInfoProvider.Instance, Substitute.For<IMediator>(), () => searchService.CreateMockScope(), NullLogger<SearchParameterDefinitionManager>.Instance);
            FilebasedSearchParameterStatusDataStore statusStore = new FilebasedSearchParameterStatusDataStore(defManager, ModelInfoProvider.Instance);

            var schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, maxSchemaVersion);

            var connectionString = new SqlConnectionStringBuilder(initialConnectionString) { InitialCatalog = databaseName }.ToString();

            var schemaOptions = new SqlServerSchemaOptions { AutomaticUpdatesEnabled = true };
            IOptions<SqlServerDataStoreConfiguration> config = Options.Create(new SqlServerDataStoreConfiguration { ConnectionString = connectionString, Initialize = true, SchemaOptions = schemaOptions, StatementTimeout = TimeSpan.FromMinutes(10) });
            var sqlRetryLogicBaseProvider = SqlConfigurableRetryFactory.CreateNoneRetryProvider();

            var sqlConnectionStringProvider = new DefaultSqlConnectionStringProvider(config);
            var defaultSqlConnectionBuilder = new DefaultSqlConnectionBuilder(sqlConnectionStringProvider, sqlRetryLogicBaseProvider);
            var securityConfiguration = new SecurityConfiguration { PrincipalClaims = { "oid" } };

            var sqlTransactionHandler = new SqlTransactionHandler();
            var defaultSqlConnectionWrapperFactory = new SqlConnectionWrapperFactory(sqlTransactionHandler, defaultSqlConnectionBuilder, sqlRetryLogicBaseProvider, config);

            SqlServerFhirModel sqlServerFhirModel = new SqlServerFhirModel(
                schemaInformation,
                defManager,
                () => statusStore,
                Options.Create(securityConfiguration),
                () => defaultSqlConnectionWrapperFactory.CreateMockScope(),
                Substitute.For<IMediator>(),
                NullLogger<SqlServerFhirModel>.Instance);

            var testHelper = new SqlServerFhirStorageTestHelper(
                initialConnectionString,
                MasterDatabaseName,
                sqlServerFhirModel,
                defaultSqlConnectionBuilder,
                null);

            var scriptProvider = new ScriptProvider<SchemaVersion>();
            var baseScriptProvider = new BaseScriptProvider();
            var mediator = Substitute.For<IMediator>();

            var schemaManagerDataStore = new SchemaManagerDataStore(
                defaultSqlConnectionWrapperFactory,
                config,
                NullLogger<SchemaManagerDataStore>.Instance);
            var schemaUpgradeRunner = new SchemaUpgradeRunner(
                scriptProvider,
                baseScriptProvider,
                NullLogger<SchemaUpgradeRunner>.Instance,
                defaultSqlConnectionWrapperFactory,
                schemaManagerDataStore);

            Func<IServiceProvider, SchemaUpgradeRunner> schemaUpgradeRunnerFactory = p => schemaUpgradeRunner;
            Func<IServiceProvider, IReadOnlySchemaManagerDataStore> schemaManagerDataStoreFactory = p => schemaManagerDataStore;
            Func<IServiceProvider, ISqlConnectionStringProvider> sqlConnectionStringProviderFunc = p => sqlConnectionStringProvider;
            Func<IServiceProvider, SqlConnectionWrapperFactory> sqlConnectionWrapperFactoryFunc = p => defaultSqlConnectionWrapperFactory;

            var collection = new ServiceCollection();
            collection.AddScoped(sqlConnectionStringProviderFunc);
            collection.AddScoped(sqlConnectionWrapperFactoryFunc);
            collection.AddScoped(schemaUpgradeRunnerFactory);
            collection.AddScoped(schemaManagerDataStoreFactory);
            var serviceProviderSchemaInitializer = collection.BuildServiceProvider();

            var schemaInitializer = new SchemaInitializer(
                serviceProviderSchemaInitializer,
                config,
                schemaInformation,
                mediator,
                NullLogger<SchemaInitializer>.Instance);

            var searchParameterToSearchValueTypeMap = new SearchParameterToSearchValueTypeMap();

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSqlServerTableRowParameterGenerators();
            serviceCollection.AddSingleton(sqlServerFhirModel);
            serviceCollection.AddSingleton<ISqlServerFhirModel>(sqlServerFhirModel);
            serviceCollection.AddSingleton(searchParameterToSearchValueTypeMap);
            var converter = (ICompressedRawResourceConverter)new CompressedRawResourceConverter();
            serviceCollection.AddSingleton(converter);
            ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();
            var upsertResourceTvpGeneratorVLatest = serviceProvider.GetRequiredService<VLatest.UpsertResourceTvpGenerator<IReadOnlyList<ResourceWrapper>>>();
            var mergeResourcesTvpGeneratorVLatest = serviceProvider.GetRequiredService<VLatest.MergeResourcesTvpGenerator<IReadOnlyList<MergeResourceWrapper>>>();
            var reindexResourceTvpGeneratorVLatest = serviceProvider.GetRequiredService<VLatest.ReindexResourceTvpGenerator<IReadOnlyList<ResourceWrapper>>>();
            var bulkReindexResourceTvpGeneratorVLatest = serviceProvider.GetRequiredService<VLatest.BulkReindexResourcesTvpGenerator<IReadOnlyList<ResourceWrapper>>>();
            var upsertSearchParamsTvpGenerator = serviceProvider.GetRequiredService<VLatest.UpsertSearchParamsTvpGenerator<List<ResourceSearchParameterStatus>>>();
            var bundleOrchestrator = new BundleOrchestrator(isEnabled: true);

            var store = new SqlServerFhirDataStore(
                sqlServerFhirModel,
                searchParameterToSearchValueTypeMap,
                upsertResourceTvpGeneratorVLatest,
                mergeResourcesTvpGeneratorVLatest,
                reindexResourceTvpGeneratorVLatest,
                bulkReindexResourceTvpGeneratorVLatest,
                Options.Create(new CoreFeatureConfiguration()),
                bundleOrchestrator,
                defaultSqlConnectionWrapperFactory,
                converter,
                NullLogger<SqlServerFhirDataStore>.Instance,
                schemaInformation,
                ModelInfoProvider.Instance,
                Substitute.For<RequestContextAccessor<IFhirRequestContext>>());

            await testHelper.CreateAndInitializeDatabase(
                databaseName,
                maxSchemaVersion,
                false,
                schemaInitializer);

            return (testHelper, defaultSqlConnectionWrapperFactory, store, sqlServerFhirModel, schemaInformation);
        }

        private async Task<string> CompareDatabaseSchemas(string databaseName1, string databaseName2)
        {
            var initialConnectionString = Environment.GetEnvironmentVariable("SqlServer:ConnectionString") ?? LocalConnectionString;

            var testConnectionString1 = new SqlConnectionStringBuilder(initialConnectionString) { InitialCatalog = databaseName1 }.ToString();
            var testConnectionString2 = new SqlConnectionStringBuilder(initialConnectionString) { InitialCatalog = databaseName2 }.ToString();

            var source = new SchemaCompareDatabaseEndpoint(testConnectionString1);
            var target = new SchemaCompareDatabaseEndpoint(testConnectionString2);
            var comparison = new SchemaComparison(source, target)
            {
                Options = { IgnoreWhitespace = true, IgnoreComments = true },
            };

            SchemaComparisonResult result = comparison.Compare();

            // These types were introduced in earlier schema versions but are no longer used in newer versions.
            // They are not removed so as to no break compatibility with instances requiring an older schema version.
            // Exclude them from the schema comparison differences.
            (string type, string name)[] deprecatedObjectToIgnore =
            {
                ("Procedure", "[dbo].[UpsertResource]"),
                ("Procedure", "[dbo].[UpsertResource_2]"),
                ("Procedure", "[dbo].[UpsertResource_3]"),
                ("Procedure", "[dbo].[UpsertResource_4]"),
                ("Procedure", "[dbo].[UpsertResource_5]"),
                ("Procedure", "[dbo].[UpsertResource_6]"),
                ("Procedure", "[dbo].[ReindexResource]"),
                ("Procedure", "[dbo].[BulkReindexResources]"),
                ("Procedure", "[dbo].[CreateTask]"),
                ("Procedure", "[dbo].[CreateTask_2]"),
                ("Procedure", "[dbo].[GetNextTask]"),
                ("Procedure", "[dbo].[GetNextTask_2]"),
                ("Procedure", "[dbo].[ResetTask]"),
                ("Procedure", "[dbo].[HardDeleteResource]"),
                ("Procedure", "[dbo].[FetchResourceChanges]"),
                ("Procedure", "[dbo].[FetchResourceChanges_2]"),
                ("Procedure", "[dbo].[RemovePartitionFromResourceChanges]"),
                ("TableType", "[dbo].[ReferenceSearchParamTableType_1]"),
                ("TableType", "[dbo].[ReferenceTokenCompositeSearchParamTableType_1]"),
                ("TableType", "[dbo].[ResourceWriteClaimTableType_1]"),
                ("TableType", "[dbo].[CompartmentAssignmentTableType_1]"),
                ("TableType", "[dbo].[ReferenceSearchParamTableType_2]"),
                ("TableType", "[dbo].[TokenSearchParamTableType_1]"),
                ("TableType", "[dbo].[TokenTextTableType_1]"),
                ("TableType", "[dbo].[StringSearchParamTableType_1]"),
                ("TableType", "[dbo].[UriSearchParamTableType_1]"),
                ("TableType", "[dbo].[NumberSearchParamTableType_1]"),
                ("TableType", "[dbo].[QuantitySearchParamTableType_1]"),
                ("TableType", "[dbo].[DateTimeSearchParamTableType_1]"),
                ("TableType", "[dbo].[ReferenceTokenCompositeSearchParamTableType_2]"),
                ("TableType", "[dbo].[TokenTokenCompositeSearchParamTableType_1]"),
                ("TableType", "[dbo].[TokenDateTimeCompositeSearchParamTableType_1]"),
                ("TableType", "[dbo].[TokenQuantityCompositeSearchParamTableType_1]"),
                ("TableType", "[dbo].[TokenStringCompositeSearchParamTableType_1]"),
                ("TableType", "[dbo].[TokenNumberNumberCompositeSearchParamTableType_1]"),
                ("TableType", "[dbo].[BulkDateTimeSearchParamTableType_1]"),
                ("TableType", "[dbo].[BulkStringSearchParamTableType_1]"),
            };

            var remainingDifferences = result.Differences.Where(
                d => !deprecatedObjectToIgnore.Any(
                    i =>
                        (d.SourceObject?.ObjectType.Name == i.type && d.SourceObject?.Name?.ToString() == i.name) ||
                        (d.TargetObject?.ObjectType.Name == i.type && d.TargetObject?.Name?.ToString() == i.name)))
                .ToList();

            var unexpectedDifference = new StringBuilder();
            foreach (SchemaDifference schemaDifference in remainingDifferences)
            {
                if (schemaDifference.Name == "SqlTable" &&
                    (schemaDifference.SourceObject.Name.ToString() == "[dbo].[DateTimeSearchParam]" ||
                    schemaDifference.SourceObject.Name.ToString() == "[dbo].[StringSearchParam]"))
                {
                    foreach (SchemaDifference child in schemaDifference.Children)
                    {
                        if (child.TargetObject == null && child.SourceObject == null && (child.Name == "PartitionColumn" || child.Name == "PartitionScheme"))
                        {
                            // The ParitionColumn and the PartitionScheme come up in the differences list even though
                            // when digging into the "difference" object the values being compared are equal.
                            continue;
                        }
                        else
                        {
                            unexpectedDifference.AppendLine($"source={child.SourceObject?.Name} target={child.TargetObject?.Name}");
                        }
                    }
                }
                else
                {
                    //// Our home grown SQL schema generator does not understand that statements can be formatted differently but contain identical SQL
                    //// Skipping queue objects
                    var objectsToSkip = new[] { "DequeueJob", "EnqueueJobs", "GetJobs", "GetResourcesByTypeAndSurrogateIdRange", "GetResourceSurrogateIdRanges", "LogEvent", "PutJobCancelation", "PutJobHeartbeat", "PutJobStatus", "CompartmentAssignment" };
                    if (schemaDifference.SourceObject != null && objectsToSkip.Any(_ => schemaDifference.SourceObject.Name.ToString().Contains(_)))
                    {
                        continue;
                    }

                    unexpectedDifference.AppendLine($"source={schemaDifference.SourceObject?.Name} target={schemaDifference.TargetObject?.Name}");
                }
            }

            // if TransactionCheckWithInitialiScript(which has current version as x-1) is not updated with the new x version then x.sql will have a wrong version inserted into SchemaVersion table
            // this will cause an entry like (x-1, started) and (x, completed) at the end of of the transaction in fullsnapshot database (testConnectionString1)
            // If any schema version is in started state then there might be a problem
            if (await SchemaVersionInStartedState(testConnectionString1) || await SchemaVersionInStartedState(testConnectionString2))
            {
                // if the test hits below statement then there is a possibility that TransactionCheckWithInitialiScript is not updated with the new version
                unexpectedDifference.AppendLine("Different SchemaVersionInStartedState.Result");
            }

            return unexpectedDifference.ToString();
        }

        public async Task<bool> SchemaVersionInStartedState(string connectionString)
        {
            using (SqlConnection sqlConnection = new SqlConnection(connectionString))
            using (SqlCommand checkSchemaVersionCommand = sqlConnection.CreateCommand())
            {
                if (sqlConnection.State != ConnectionState.Open)
                {
                    await sqlConnection.OpenAsync(CancellationToken.None);
                }

                checkSchemaVersionCommand.CommandText = "SELECT count(*) FROM SchemaVersion where Status = 'started'";
                if ((int?)await checkSchemaVersionCommand.ExecuteScalarAsync(CancellationToken.None) >= 1)
                {
                    return true;
                }
            }

            return false;
        }

        private async Task CheckTableDataAsync(string tableName, string columnsString, SqlConnectionWrapperFactory sourceFactory, SqlConnectionWrapperFactory targetFactory, long startSurrogateId, long endSurrogateId)
        {
            using SqlConnectionWrapper sourceConnection = await sourceFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None);
            using SqlDataAdapter sourceAdapter = new SqlDataAdapter();

            using SqlConnectionWrapper targetConnection = await targetFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None);
            using SqlDataAdapter targetAdapter = new SqlDataAdapter();

            string queryText = $"select {columnsString} from {tableName} where ResourceSurrogateId >= {startSurrogateId} and ResourceSurrogateId < {endSurrogateId}";

            sourceAdapter.SelectCommand = new SqlCommand(queryText, sourceConnection.SqlConnection);
            DataSet sourceResult = new DataSet();
            sourceAdapter.Fill(sourceResult);

            targetAdapter.SelectCommand = new SqlCommand(queryText, targetConnection.SqlConnection);
            DataSet targetResult = new DataSet();
            targetAdapter.Fill(targetResult);

            Assert.Equal(sourceResult.Tables[0].Columns.Count, targetResult.Tables[0].Columns.Count);
            Assert.Equal(sourceResult.Tables[0].Rows.Count, targetResult.Tables[0].Rows.Count);
        }
    }
}

﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.IO;
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
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Schema.Manager;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.Test.Utilities;
using Microsoft.SqlServer.Dac.Compare;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Schema)]
    public class SqlServerSchemaUpgradeTests
    {
        private const string MasterDatabaseName = "master";

        public SqlServerSchemaUpgradeTests()
        {
        }

        [Fact]
        public async Task GivenTwoSchemaInitializationMethods_WhenCreatingTwoDatabases_BothSchemasShouldBeEquivalent()
        {
            // previously test was skipped with (Skip = "Issue connecting with SQL workload identity & custom auth provider. AB#122858")
            if (!EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.SqlServerConnectionString).Contains("(local)", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var snapshotDatabaseName = SqlServerFhirStorageTestsFixture.GetDatabaseName($"Upgrade_Snapshot");
            var diffDatabaseName = SqlServerFhirStorageTestsFixture.GetDatabaseName($"Upgrade_Diff");

            SqlServerFhirStorageTestHelper testHelper1 = null;
            SqlServerFhirStorageTestHelper testHelper2 = null;
            try
            {
                // Create two databases, one where we apply the the maximum supported version's snapshot SQL schema file
                (testHelper1, _) = await SetupTestHelperAndCreateDatabase(
                    snapshotDatabaseName,
                    SchemaVersionConstants.Max,
                    forceIncrementalSchemaUpgrade: false);

                // And one where we apply .diff.sql files to upgrade the schema version to the maximum supported version.
                (testHelper2, _) = await SetupTestHelperAndCreateDatabase(
                    diffDatabaseName,
                    SchemaVersionConstants.Max,
                    forceIncrementalSchemaUpgrade: true);

                var diff = await CompareDatabaseSchemas(snapshotDatabaseName, diffDatabaseName);
                Assert.True(string.IsNullOrEmpty(diff), diff);
            }
            finally
            {
                await (testHelper1?.DeleteDatabase(snapshotDatabaseName) ?? Task.CompletedTask);
                await (testHelper2?.DeleteDatabase(diffDatabaseName) ?? Task.CompletedTask);
            }
        }

        [Fact]
        public void GivenASchemaVersion_WhenApplyingDiffTwice_ShouldSucceed()
        {
            var versions = Enum.GetValues(typeof(SchemaVersion)).OfType<object>().ToList().Select(x => Convert.ToInt32(x)).ToList();
            Parallel.ForEach(versions, async version =>
            {
                // The schema upgrade scripts starting from v7 were made idempotent.
                if (version >= 7 && version >= SchemaVersionConstants.MinForUpgrade) // no sense in checking not supported versions
                {
                    var snapshotDatabaseName = SqlServerFhirStorageTestsFixture.GetDatabaseName($"Upgrade_Snapshot_Diff");

                    SqlServerFhirStorageTestHelper testHelper = null;
                    SchemaUpgradeRunner upgradeRunner;

                    try
                    {
                        (testHelper, upgradeRunner) = await SetupTestHelperAndCreateDatabase(
                            snapshotDatabaseName,
                            version - 1,
                            forceIncrementalSchemaUpgrade: false);

                        await upgradeRunner.ApplySchemaAsync(version, applyFullSchemaSnapshot: false, CancellationToken.None);
                        await upgradeRunner.ApplySchemaAsync(version, applyFullSchemaSnapshot: false, CancellationToken.None);
                    }
                    finally
                    {
                        await testHelper.DeleteDatabase(snapshotDatabaseName);
                    }
                }
            });
        }

        private async Task<(SqlServerFhirStorageTestHelper testHelper, SchemaUpgradeRunner upgradeRunner)> SetupTestHelperAndCreateDatabase(string databaseName, int maxSchemaVersion, bool forceIncrementalSchemaUpgrade)
        {
            var initialConnectionString = EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.SqlServerConnectionString);
            var searchService = Substitute.For<ISearchService>();
            ISearchParameterDefinitionManager defManager = new SearchParameterDefinitionManager(ModelInfoProvider.Instance, Substitute.For<IMediator>(), searchService.CreateMockScopeProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);
            FilebasedSearchParameterStatusDataStore statusStore = new FilebasedSearchParameterStatusDataStore(defManager, ModelInfoProvider.Instance);

            var schemaInformation = new SchemaInformation(SchemaVersionConstants.MinForUpgrade, maxSchemaVersion);

            var connectionString = new SqlConnectionStringBuilder(initialConnectionString) { InitialCatalog = databaseName }.ToString();

            var schemaOptions = new SqlServerSchemaOptions { AutomaticUpdatesEnabled = true };
            IOptions<SqlServerDataStoreConfiguration> config = Options.Create(new SqlServerDataStoreConfiguration { ConnectionString = connectionString, Initialize = true, SchemaOptions = schemaOptions, StatementTimeout = TimeSpan.FromMinutes(10) });
            var sqlRetryLogicBaseProvider = SqlConfigurableRetryFactory.CreateNoneRetryProvider();

            var defaultSqlConnectionBuilder = new DefaultSqlConnectionBuilder(config, sqlRetryLogicBaseProvider);
            var securityConfiguration = new SecurityConfiguration { PrincipalClaims = { "oid" } };

            var sqlTransactionHandler = new SqlTransactionHandler();
            var defaultSqlConnectionWrapperFactory = new SqlConnectionWrapperFactory(sqlTransactionHandler, defaultSqlConnectionBuilder, sqlRetryLogicBaseProvider, config);

            SqlServerFhirModel sqlServerFhirModel = new SqlServerFhirModel(
                schemaInformation,
                defManager,
                () => statusStore,
                Options.Create(securityConfiguration),
                defaultSqlConnectionWrapperFactory.CreateMockScopeProvider(),
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
            Func<IServiceProvider, SqlConnectionWrapperFactory> sqlConnectionWrapperFactoryFunc = p => defaultSqlConnectionWrapperFactory;

            var collection = new ServiceCollection();
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

            await testHelper.CreateAndInitializeDatabase(
                databaseName,
                maxSchemaVersion,
                forceIncrementalSchemaUpgrade,
                schemaInitializer);

            return (testHelper, schemaUpgradeRunner);
        }

        private async Task<string> CompareDatabaseSchemas(string databaseName1, string databaseName2)
        {
            var initialConnectionString = EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.SqlServerConnectionString);
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
                ("Procedure", "[dbo].[RebuildIndex]"),
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
                ("Procedure", "[dbo].[HardDeleteResource_2]"),
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
                d => d != null && !deprecatedObjectToIgnore.Any(
                    i =>
                        (d.SourceObject?.ObjectType?.Name == i.type && d.SourceObject?.Name?.ToString() == i.name) ||
                        (d.TargetObject?.ObjectType?.Name == i.type && d.TargetObject?.Name?.ToString() == i.name)))
                .ToList();

            var unexpectedDifference = new StringBuilder();
            foreach (SchemaDifference schemaDifference in remainingDifferences)
            {
                var objectsToSkip = new[] { "PartitionFunction_ResourceChangeData_Timestamp" }; // definition is not predictable as it has start time component
                ////var objectsToSkip = new[] { "GetResourceSearchParamStats", "MergeResourcesAdvanceTransactionVisibility", "DequeueJob", "DisableIndexes", "GetResourceVersions", "CleanupEventLog", "InitDefrag", "EnqueueJobs", "GetResourcesByTypeAndSurrogateIdRange", "GetResourceSurrogateIdRanges", "GetCommandsForRebuildIndexes", "GetIndexCommands", "SwitchPartitionsIn", "SwitchPartitionsOut" }.ToList();
                if (schemaDifference.SourceObject != null && objectsToSkip.Any(_ => schemaDifference.SourceObject.Name.ToString().Contains(_)))
                {
                    continue;
                }

                if (schemaDifference.SourceObject != null
                    && schemaDifference.TargetObject != null
                    && schemaDifference.SourceObject.ObjectType.Name == "Procedure"
                    && await IsObjectTextEqual(testConnectionString1, testConnectionString2, schemaDifference.SourceObject.Name.ToString()))
                {
                    continue;
                }

                unexpectedDifference.AppendLine($"source={schemaDifference.SourceObject?.Name} target={schemaDifference.TargetObject?.Name}");
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

        private async Task<bool> IsObjectTextEqual(string connStr1, string connStr2, string objectName)
        {
            var text1 = await GetObjectText(connStr1, objectName);
            var text2 = await GetObjectText(connStr2, objectName);

            text1 = Normalize(text1);
            text2 = Normalize(text2);

            try
            {
                Assert.Equal(text1, text2);
            }
            catch (Xunit.Sdk.EqualException e)
            {
                throw new Exception($"object={objectName} message={e.Message}");
            }

            return true;
        }

        private string Normalize(string text)
        {
            using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(text));
            using var reader = new StreamReader(inputStream);
            using var outputStream = new MemoryStream();
            using var writer = new StreamWriter(outputStream);
            new Sql150ScriptGenerator().GenerateScript(new TSql150Parser(true).Parse(reader, out var _), writer);
            writer.Flush();
            text = Encoding.UTF8.GetString(outputStream.ToArray());
            return text;
        }

        private async Task<string> GetObjectText(string connStr, string objectName)
        {
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            using var cmd = new SqlCommand("dbo.sp_helptext", conn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@objname", objectName);
            using var reader = cmd.ExecuteReader();
            var text = string.Empty;
            while (reader.Read())
            {
                text += reader.GetString(0);
            }

            return text;
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
    }
}

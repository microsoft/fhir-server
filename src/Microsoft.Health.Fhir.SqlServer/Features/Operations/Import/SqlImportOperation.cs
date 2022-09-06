// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.SqlServer.Features.Operations.Import;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.JobManagement;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Schema.Model;
using Xunit;
using Index = Microsoft.Health.SqlServer.Features.Schema.Model.Index;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public class SqlImportOperation : ISqlImportOperation, IImportOrchestratorJobDataStoreOperation
    {
        private SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private ISqlServerFhirModel _model;
        private readonly ImportTaskConfiguration _importTaskConfiguration;
        private readonly SchemaInformation _schemaInformation;
        private ILogger<SqlImportOperation> _logger;

        public SqlImportOperation(
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            ISqlServerFhirModel model,
            IOptions<OperationsConfiguration> operationsConfig,
            SchemaInformation schemaInformation,
            ILogger<SqlImportOperation> logger)
        {
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(model, nameof(model));
            EnsureArg.IsNotNull(operationsConfig, nameof(operationsConfig));
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _model = model;
            _importTaskConfiguration = operationsConfig.Value.Import;
            _schemaInformation = schemaInformation;
            _logger = logger;
        }

        public IReadOnlyList<(Table table, Index index, bool pageCompression)> OptionalUniqueIndexesForImport { get; private set; }

        public IReadOnlyList<(Table table, Index index, bool pageCompression)> OptionalIndexesForImport { get; private set; }

        public static IReadOnlyList<string> SearchParameterTables { get; } =
            new List<string>()
            {
                VLatest.CompartmentAssignment.TableName,
                VLatest.ReferenceSearchParam.TableName,
                VLatest.TokenSearchParam.TableName,
                VLatest.TokenText.TableName,
                VLatest.StringSearchParam.TableName,
                VLatest.UriSearchParam.TableName,
                VLatest.NumberSearchParam.TableName,
                VLatest.QuantitySearchParam.TableName,
                VLatest.DateTimeSearchParam.TableName,
                VLatest.ReferenceTokenCompositeSearchParam.TableName,
                VLatest.TokenTokenCompositeSearchParam.TableName,
                VLatest.TokenDateTimeCompositeSearchParam.TableName,
                VLatest.TokenQuantityCompositeSearchParam.TableName,
                VLatest.TokenStringCompositeSearchParam.TableName,
                VLatest.TokenNumberNumberCompositeSearchParam.TableName,
            };

        public IReadOnlyList<(Table table, Index index, bool pageCompression)> UniqueIndexesList()
        {
            var list = new List<(Table table, Index index, bool pageCompression)>();

            if (_schemaInformation.Current >= SchemaVersionConstants.RenamedIndexForResourceTable)
            {
                // Do nothing. This is the easiest fix until we remove this code completely.
            }
            else if (_schemaInformation.Current >= SchemaVersionConstants.AddPrimaryKeyForResourceTable)
            {
                list.Add((V25.Resource, V25.Resource.UQIX_Resource_ResourceSurrogateId, false));
            }

            list.Add((VLatest.Resource, VLatest.Resource.IX_Resource_ResourceTypeId_ResourceId, false));
            list.Add((VLatest.Resource, VLatest.Resource.IX_Resource_ResourceTypeId_ResourceSurrgateId, false));

            return list;
        }

        public IReadOnlyList<(Table table, Index index, bool pageCompression)> IndexesList()
        {
            var list = new List<(Table table, Index index, bool pageCompression)>();

            if (_schemaInformation.Current < SchemaVersionConstants.AddPrimaryKeyForResourceTable)
            {
                list.Add((V24.Resource, V24.Resource.IX_Resource_ResourceSurrogateId, false));
            }

            list.Add((VLatest.CompartmentAssignment, VLatest.CompartmentAssignment.IX_CompartmentAssignment_CompartmentTypeId_ReferenceResourceId, true));
            list.Add((VLatest.DateTimeSearchParam, VLatest.DateTimeSearchParam.IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime, false));
            list.Add((VLatest.DateTimeSearchParam, VLatest.DateTimeSearchParam.IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime_Long, false));
            list.Add((VLatest.DateTimeSearchParam, VLatest.DateTimeSearchParam.IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime, false));
            list.Add((VLatest.DateTimeSearchParam, VLatest.DateTimeSearchParam.IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime_Long, false));
            list.Add((VLatest.NumberSearchParam, VLatest.NumberSearchParam.IX_NumberSearchParam_SearchParamId_HighValue_LowValue, false));
            list.Add((VLatest.NumberSearchParam, VLatest.NumberSearchParam.IX_NumberSearchParam_SearchParamId_LowValue_HighValue, false));
            list.Add((VLatest.NumberSearchParam, VLatest.NumberSearchParam.IX_NumberSearchParam_SearchParamId_SingleValue, false));
            list.Add((VLatest.QuantitySearchParam, VLatest.QuantitySearchParam.IX_QuantitySearchParam_SearchParamId_QuantityCodeId_HighValue_LowValue, false));
            list.Add((VLatest.QuantitySearchParam, VLatest.QuantitySearchParam.IX_QuantitySearchParam_SearchParamId_QuantityCodeId_LowValue_HighValue, false));
            list.Add((VLatest.QuantitySearchParam, VLatest.QuantitySearchParam.IX_QuantitySearchParam_SearchParamId_QuantityCodeId_SingleValue, false));
            list.Add((VLatest.ReferenceSearchParam, VLatest.ReferenceSearchParam.IX_ReferenceSearchParam_SearchParamId_ReferenceResourceTypeId_ReferenceResourceId_BaseUri_ReferenceResourceVersion, true));
            list.Add((VLatest.ReferenceTokenCompositeSearchParam, VLatest.ReferenceTokenCompositeSearchParam.IX_ReferenceTokenCompositeSearchParam_ReferenceResourceId1_Code2, true));
            list.Add((VLatest.StringSearchParam, VLatest.StringSearchParam.IX_StringSearchParam_SearchParamId_Text, true));
            list.Add((VLatest.StringSearchParam, VLatest.StringSearchParam.IX_StringSearchParam_SearchParamId_TextWithOverflow, true));
            list.Add((VLatest.TokenDateTimeCompositeSearchParam, VLatest.TokenDateTimeCompositeSearchParam.IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2, true));
            list.Add((VLatest.TokenDateTimeCompositeSearchParam, VLatest.TokenDateTimeCompositeSearchParam.IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2_Long, true));
            list.Add((VLatest.TokenDateTimeCompositeSearchParam, VLatest.TokenDateTimeCompositeSearchParam.IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2, true));
            list.Add((VLatest.TokenDateTimeCompositeSearchParam, VLatest.TokenDateTimeCompositeSearchParam.IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2_Long, true));
            list.Add((VLatest.TokenNumberNumberCompositeSearchParam, VLatest.TokenNumberNumberCompositeSearchParam.IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_LowValue2_HighValue2_LowValue3_HighValue3, true));
            list.Add((VLatest.TokenNumberNumberCompositeSearchParam, VLatest.TokenNumberNumberCompositeSearchParam.IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_Text2, true));
            list.Add((VLatest.TokenQuantityCompositeSearchParam, VLatest.TokenQuantityCompositeSearchParam.IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_HighValue2_LowValue2, true));
            list.Add((VLatest.TokenQuantityCompositeSearchParam, VLatest.TokenQuantityCompositeSearchParam.IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_LowValue2_HighValue2, true));
            list.Add((VLatest.TokenQuantityCompositeSearchParam, VLatest.TokenQuantityCompositeSearchParam.IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_SingleValue2, true));
            list.Add((VLatest.TokenSearchParam, VLatest.TokenSearchParam.IX_TokenSeachParam_SearchParamId_Code_SystemId, true));
            list.Add((VLatest.TokenStringCompositeSearchParam, VLatest.TokenStringCompositeSearchParam.IX_TokenStringCompositeSearchParam_SearchParamId_Code1_Text2, true));
            list.Add((VLatest.TokenStringCompositeSearchParam, VLatest.TokenStringCompositeSearchParam.IX_TokenStringCompositeSearchParam_SearchParamId_Code1_Text2WithOverflow, true));
            list.Add((VLatest.TokenText, VLatest.TokenText.IX_TokenText_SearchParamId_Text, true));
            list.Add((VLatest.TokenTokenCompositeSearchParam, VLatest.TokenTokenCompositeSearchParam.IX_TokenTokenCompositeSearchParam_Code1_Code2, true));
            list.Add((VLatest.UriSearchParam, VLatest.UriSearchParam.IX_UriSearchParam_SearchParamId_Uri, true));

            // ResourceWriteClaim Table - No unclustered index

            return list;
        }

        public async Task BulkCopyDataAsync(DataTable dataTable, CancellationToken cancellationToken)
        {
            try
            {
                using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
                using (SqlBulkCopy bulkCopy = new SqlBulkCopy(sqlConnectionWrapper.SqlConnection, SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.UseInternalTransaction | SqlBulkCopyOptions.KeepNulls, null))
                {
                    bulkCopy.DestinationTableName = dataTable.TableName;
                    bulkCopy.BatchSize = dataTable.Rows.Count;

                    bulkCopy.BulkCopyTimeout = _importTaskConfiguration.SqlBulkOperationTimeoutInSec;
                    await bulkCopy.WriteToServerAsync(dataTable.CreateDataReader(), cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "BulkCopyDataAsync failed.");

                throw new RetriableJobException(ex.Message, ex);
            }
        }

        public async Task<IEnumerable<SqlBulkCopyDataWrapper>> BulkMergeResourceAsync(IEnumerable<SqlBulkCopyDataWrapper> resources, CancellationToken cancellationToken)
        {
            try
            {
                List<long> importedSurrogatedId = new List<long>();

                // Make sure there's no dup in this batch
                resources = resources.GroupBy(r => (r.ResourceTypeId, r.Resource.ResourceId)).Select(r => r.First());
                IEnumerable<BulkImportResourceTypeV1Row> inputResources = resources.Select(r => r.BulkImportResource);

                using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
                using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
                {
                    VLatest.BulkMergeResource.PopulateCommand(sqlCommandWrapper, inputResources);
                    sqlCommandWrapper.CommandTimeout = _importTaskConfiguration.SqlBulkOperationTimeoutInSec;

                    var sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);

                    while (await sqlDataReader.ReadAsync(cancellationToken))
                    {
                        long surrogatedId = sqlDataReader.GetInt64(0);
                        importedSurrogatedId.Add(surrogatedId);
                    }

                    return resources.Where(r => importedSurrogatedId.Contains(r.ResourceSurrogateId));
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "BulkMergeResourceAsync failed.");
                throw new RetriableJobException(ex.Message, ex);
            }
        }

        public async Task CleanBatchResourceAsync(string resourceType, long beginSequenceId, long endSequenceId, CancellationToken cancellationToken)
        {
            try
            {
                short resourceTypeId = _model.GetResourceTypeId(resourceType);

                await BatchDeleteResourcesInternalAsync(beginSequenceId, endSequenceId, resourceTypeId, _importTaskConfiguration.SqlCleanResourceBatchSize, cancellationToken);
                await BatchDeleteResourceWriteClaimsInternalAsync(beginSequenceId, endSequenceId, _importTaskConfiguration.SqlCleanResourceBatchSize, cancellationToken);

                foreach (var tableName in SearchParameterTables.ToArray())
                {
                    await BatchDeleteResourceParamsInternalAsync(tableName, beginSequenceId, endSequenceId, resourceTypeId, _importTaskConfiguration.SqlCleanResourceBatchSize, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "CleanBatchResourceAsync failed.");
                if (ex.IsRetryable())
                {
                    throw new RetriableJobException(ex.Message, ex);
                }

                throw;
            }
        }

        public async Task PreprocessAsync(CancellationToken cancellationToken)
        {
            try
            {
                await InitializeIndexProperties(cancellationToken);
                OptionalIndexesForImport = IndexesList();
                OptionalUniqueIndexesForImport = UniqueIndexesList();

                // Not disable index by default
                if (_importTaskConfiguration.DisableOptionalIndexesForImport || _importTaskConfiguration.DisableUniqueOptionalIndexesForImport)
                {
                    List<(string tableName, string indexName)> indexesNeedDisable = new List<(string tableName, string indexName)>();

                    if (_importTaskConfiguration.DisableOptionalIndexesForImport)
                    {
                        indexesNeedDisable.AddRange(OptionalIndexesForImport.Select(indexRecord => (indexRecord.table.TableName, indexRecord.index.IndexName)));
                    }

                    if (_importTaskConfiguration.DisableUniqueOptionalIndexesForImport)
                    {
                        indexesNeedDisable.AddRange(OptionalUniqueIndexesForImport.Select(indexRecord => (indexRecord.table.TableName, indexRecord.index.IndexName)));
                    }

                    foreach (var index in indexesNeedDisable)
                    {
                        using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
                        using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
                        {
                            VLatest.DisableIndex.PopulateCommand(sqlCommandWrapper, index.tableName, index.indexName);
                            await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "PreprocessAsync failed.");
                if (ex.IsRetryable())
                {
                    throw new RetriableJobException(ex.Message, ex);
                }

                throw;
            }
        }

        public async Task PostprocessAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Not rerebuild index by default
                if (_importTaskConfiguration.DisableOptionalIndexesForImport || _importTaskConfiguration.DisableUniqueOptionalIndexesForImport)
                {
                    IList<(string tableName, string indexName, string command)> commandsForRebuildIndexes = new List<(string tableName, string indexName, string command)>();
                    await SwitchPartitionsOutAllTables(_importTaskConfiguration.RebuildClustered, cancellationToken);
                    commandsForRebuildIndexes = await GetCommandsForRebuildIndexes(_importTaskConfiguration.RebuildClustered, cancellationToken);
                    if (_importTaskConfiguration.RebuildClustered)
                    {
                        commandsForRebuildIndexes = await GetCommandsForRebuildIndexes(false, cancellationToken);
                    }

                    await RunCommandForRebuildIndexes(commandsForRebuildIndexes, cancellationToken);
                    await SwitchPartitionsInAllTables(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "PostprocessAsync failed.");
                if (ex.IsRetryable())
                {
                    throw new RetriableJobException(ex.Message, ex);
                }

                throw;
            }
        }

        private async Task InitializeIndexProperties(CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                sqlCommandWrapper.CommandTimeout = _importTaskConfiguration.SqlLongRunningOperationTimeoutInSec;

                VLatest.InitializeIndexProperties.PopulateCommand(sqlCommandWrapper);
                await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        private async Task<IList<(string tableName, string indexName, string command)>> GetCommandsForRebuildIndexes(bool rebuildClustered, CancellationToken cancellationToken)
        {
            var indexes = new List<(string tableName, string indexName, string command)>();
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                sqlCommandWrapper.CommandTimeout = _importTaskConfiguration.SqlLongRunningOperationTimeoutInSec;

                VLatest.GetCommandsForRebuildIndexes.PopulateCommand(sqlCommandWrapper, rebuildClustered);
                using SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);
                while (await sqlDataReader.ReadAsync(cancellationToken))
                {
                    var tableName = sqlDataReader.GetString(0);
                    var indexName = sqlDataReader.GetString(1);
                    var command = sqlDataReader.GetString(2);
                    indexes.Add((tableName, indexName, command));
                }
            }

            return indexes;
        }

        private async Task RunCommandForRebuildIndexes(IList<(string tableName, string indexName, string command)> commands, CancellationToken cancellationToken)
        {
            var tasks = new Queue<Task<string>>();
            try
            {
                foreach (var sqlCommand in commands)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException("Operation Cancel");
                    }

                    while (tasks.Count >= _importTaskConfiguration.SqlMaxRebuildIndexOperationConcurrentCount)
                    {
                        await tasks.First();
                        _ = tasks.Dequeue();
                    }

                    tasks.Enqueue(ExecuteSqlCommand(sqlCommand.tableName, sqlCommand.indexName, sqlCommand.command, CancellationToken.None));
                }

                while (tasks.Count > 0)
                {
                    await tasks.First();
                    _ = tasks.Dequeue();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rebuild indexes");
                if (ex.IsRetryable())
                {
                    throw new RetriableJobException(ex.Message, ex);
                }

                throw;
            }
        }

        private async Task<string> ExecuteSqlCommand(string tableName, string indexName, string command, CancellationToken cancellationToken)
        {
            try
            {
                using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
                using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
                {
                    sqlCommandWrapper.CommandTimeout = _importTaskConfiguration.SqlLongRunningOperationTimeoutInSec;

                    VLatest.ExecuteCommandForRebuildIndexes.PopulateCommand(sqlCommandWrapper, tableName, indexName, command);
                    using SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);
                    while (await sqlDataReader.ReadAsync(cancellationToken))
                    {
                        indexName = sqlDataReader.GetString(0);
                    }
                }

                await Task.Delay(1000, cancellationToken);
            }
            catch (SqlException)
            {
                throw;
            }

            return indexName;
        }

        private async Task SwitchPartitionsOutAllTables(bool rebuildClustered, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                sqlCommandWrapper.CommandTimeout = _importTaskConfiguration.SqlLongRunningOperationTimeoutInSec;

                VLatest.SwitchPartitionsOutAllTables.PopulateCommand(sqlCommandWrapper, rebuildClustered);
                await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        private async Task SwitchPartitionsInAllTables(CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                sqlCommandWrapper.CommandTimeout = _importTaskConfiguration.SqlLongRunningOperationTimeoutInSec;

                VLatest.SwitchPartitionsInAllTables.PopulateCommand(sqlCommandWrapper);
                await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        private async Task BatchDeleteResourcesInternalAsync(long beginSequenceId, long endSequenceId, short resourceTypeId, int batchSize, CancellationToken cancellationToken)
        {
            while (true)
            {
                using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
                using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
                {
                    sqlCommandWrapper.CommandTimeout = _importTaskConfiguration.SqlBulkOperationTimeoutInSec;

                    VLatest.BatchDeleteResources.PopulateCommand(sqlCommandWrapper, resourceTypeId, beginSequenceId, endSequenceId, batchSize);
                    int impactRows = await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);

                    if (impactRows < batchSize)
                    {
                        return;
                    }
                }
            }
        }

        private async Task BatchDeleteResourceWriteClaimsInternalAsync(long beginSequenceId, long endSequenceId, int batchSize, CancellationToken cancellationToken)
        {
            while (true)
            {
                using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
                using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
                {
                    sqlCommandWrapper.CommandTimeout = _importTaskConfiguration.SqlBulkOperationTimeoutInSec;

                    VLatest.BatchDeleteResourceWriteClaims.PopulateCommand(sqlCommandWrapper, beginSequenceId, endSequenceId, batchSize);
                    int impactRows = await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);

                    if (impactRows < batchSize)
                    {
                        return;
                    }
                }
            }
        }

        private async Task BatchDeleteResourceParamsInternalAsync(string tableName, long beginSequenceId, long endSequenceId, short resourceTypeId, int batchSize, CancellationToken cancellationToken)
        {
            while (true)
            {
                using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
                using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
                {
                    sqlCommandWrapper.CommandTimeout = _importTaskConfiguration.SqlBulkOperationTimeoutInSec;

                    VLatest.BatchDeleteResourceParams.PopulateCommand(sqlCommandWrapper, tableName, resourceTypeId, beginSequenceId, endSequenceId, batchSize);
                    int impactRows = await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);

                    if (impactRows < batchSize)
                    {
                        return;
                    }
                }
            }
        }
    }
}

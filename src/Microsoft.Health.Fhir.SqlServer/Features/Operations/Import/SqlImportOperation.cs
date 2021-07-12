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
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema.Model;
using Microsoft.IO;
using Index = Microsoft.Health.SqlServer.Features.Schema.Model.Index;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public class SqlImportOperation : ISqlImportOperation, IImportOrchestratorTaskDataStoreOperation
    {
        private SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private ISqlServerTransientFaultRetryPolicyFactory _sqlServerTransientFaultRetryPolicyFactory;
        private SqlServerFhirModel _model;
        private readonly RecyclableMemoryStreamManager _memoryStreamManager;
        private readonly ImportTaskConfiguration _importTaskConfiguration;
        private ILogger<SqlImportOperation> _logger;

        public SqlImportOperation(
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            ISqlServerTransientFaultRetryPolicyFactory sqlServerTransientFaultRetryPolicyFactory,
            SqlServerFhirModel model,
            IOptions<OperationsConfiguration> operationsConfig,
            ILogger<SqlImportOperation> logger)
        {
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(sqlServerTransientFaultRetryPolicyFactory, nameof(sqlServerTransientFaultRetryPolicyFactory));
            EnsureArg.IsNotNull(model, nameof(model));
            EnsureArg.IsNotNull(operationsConfig, nameof(operationsConfig));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _sqlServerTransientFaultRetryPolicyFactory = sqlServerTransientFaultRetryPolicyFactory;
            _model = model;
            _importTaskConfiguration = operationsConfig.Value.Import;
            _logger = logger;

            _memoryStreamManager = new RecyclableMemoryStreamManager();
        }

        public static IReadOnlyList<(Table table, Index index)> OptionalUniqueIndexesForImport { get; } =
            new List<(Table table, Index index)>()
            {
                (VLatest.Resource, VLatest.Resource.IX_Resource_ResourceTypeId_ResourceId),
                (VLatest.Resource, VLatest.Resource.IX_Resource_ResourceTypeId_ResourceSurrgateId),
            };

        public static IReadOnlyList<(Table table, Index index)> OptionalIndexesForImport { get; } =
            new List<(Table table, Index index)>()
            {
                (VLatest.Resource, VLatest.Resource.IX_Resource_ResourceSurrogateId),
                (VLatest.CompartmentAssignment, VLatest.CompartmentAssignment.IX_CompartmentAssignment_CompartmentTypeId_ReferenceResourceId),
                (VLatest.DateTimeSearchParam, VLatest.DateTimeSearchParam.IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime),
                (VLatest.DateTimeSearchParam, VLatest.DateTimeSearchParam.IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime_Long),
                (VLatest.DateTimeSearchParam, VLatest.DateTimeSearchParam.IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime),
                (VLatest.DateTimeSearchParam, VLatest.DateTimeSearchParam.IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime_Long),
                (VLatest.NumberSearchParam, VLatest.NumberSearchParam.IX_NumberSearchParam_SearchParamId_HighValue_LowValue),
                (VLatest.NumberSearchParam, VLatest.NumberSearchParam.IX_NumberSearchParam_SearchParamId_LowValue_HighValue),
                (VLatest.NumberSearchParam, VLatest.NumberSearchParam.IX_NumberSearchParam_SearchParamId_SingleValue),
                (VLatest.QuantitySearchParam, VLatest.QuantitySearchParam.IX_QuantitySearchParam_SearchParamId_QuantityCodeId_HighValue_LowValue),
                (VLatest.QuantitySearchParam, VLatest.QuantitySearchParam.IX_QuantitySearchParam_SearchParamId_QuantityCodeId_LowValue_HighValue),
                (VLatest.QuantitySearchParam, VLatest.QuantitySearchParam.IX_QuantitySearchParam_SearchParamId_QuantityCodeId_SingleValue),
                (VLatest.ReferenceSearchParam, VLatest.ReferenceSearchParam.IX_ReferenceSearchParam_SearchParamId_ReferenceResourceTypeId_ReferenceResourceId_BaseUri_ReferenceResourceVersion),
                (VLatest.ReferenceTokenCompositeSearchParam, VLatest.ReferenceTokenCompositeSearchParam.IX_ReferenceTokenCompositeSearchParam_ReferenceResourceId1_Code2),
                (VLatest.StringSearchParam, VLatest.StringSearchParam.IX_StringSearchParam_SearchParamId_Text),
                (VLatest.StringSearchParam, VLatest.StringSearchParam.IX_StringSearchParam_SearchParamId_TextWithOverflow),
                (VLatest.TokenDateTimeCompositeSearchParam, VLatest.TokenDateTimeCompositeSearchParam.IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2),
                (VLatest.TokenDateTimeCompositeSearchParam, VLatest.TokenDateTimeCompositeSearchParam.IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2_Long),
                (VLatest.TokenDateTimeCompositeSearchParam, VLatest.TokenDateTimeCompositeSearchParam.IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2),
                (VLatest.TokenDateTimeCompositeSearchParam, VLatest.TokenDateTimeCompositeSearchParam.IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2_Long),
                (VLatest.TokenNumberNumberCompositeSearchParam, VLatest.TokenNumberNumberCompositeSearchParam.IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_LowValue2_HighValue2_LowValue3_HighValue3),
                (VLatest.TokenNumberNumberCompositeSearchParam, VLatest.TokenNumberNumberCompositeSearchParam.IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_Text2),
                (VLatest.TokenQuantityCompositeSearchParam, VLatest.TokenQuantityCompositeSearchParam.IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_HighValue2_LowValue2),
                (VLatest.TokenQuantityCompositeSearchParam, VLatest.TokenQuantityCompositeSearchParam.IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_LowValue2_HighValue2),
                (VLatest.TokenQuantityCompositeSearchParam, VLatest.TokenQuantityCompositeSearchParam.IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_SingleValue2),
                (VLatest.TokenSearchParam, VLatest.TokenSearchParam.IX_TokenSeachParam_SearchParamId_Code_SystemId),
                (VLatest.TokenStringCompositeSearchParam, VLatest.TokenStringCompositeSearchParam.IX_TokenStringCompositeSearchParam_SearchParamId_Code1_Text2),
                (VLatest.TokenStringCompositeSearchParam, VLatest.TokenStringCompositeSearchParam.IX_TokenStringCompositeSearchParam_SearchParamId_Code1_Text2WithOverflow),
                (VLatest.TokenText, VLatest.TokenText.IX_TokenText_SearchParamId_Text),
                (VLatest.TokenTokenCompositeSearchParam, VLatest.TokenTokenCompositeSearchParam.IX_TokenTokenCompositeSearchParam_Code1_Code2),
                (VLatest.UriSearchParam, VLatest.UriSearchParam.IX_UriSearchParam_SearchParamId_Uri),

                // ResourceWriteClaim Table - No unclustered index
            };

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

        public async Task BulkCopyDataAsync(DataTable dataTable, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlBulkCopy bulkCopy = new SqlBulkCopy(sqlConnectionWrapper.SqlConnection, SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.UseInternalTransaction | SqlBulkCopyOptions.KeepNulls, null))
            {
                bulkCopy.DestinationTableName = dataTable.TableName;
                bulkCopy.BatchSize = dataTable.Rows.Count;

                try
                {
                    await _sqlServerTransientFaultRetryPolicyFactory.Create().ExecuteAsync(
                        async () =>
                        {
                            bulkCopy.BulkCopyTimeout = _importTaskConfiguration.SqlBulkOperationTimeoutInSec;
                            await bulkCopy.WriteToServerAsync(dataTable.CreateDataReader());
                        });
                }
                catch (Exception ex)
                {
                    _logger.LogInformation(ex, "Failed to bulk copy data.");

                    throw;
                }
            }
        }

        public async Task<IEnumerable<SqlBulkCopyDataWrapper>> BulkMergeResourceAsync(IEnumerable<SqlBulkCopyDataWrapper> resources, CancellationToken cancellationToken)
        {
            List<long> importedSurrogatedId = new List<long>();

            // Make sure there's no dup in this batch
            resources = resources.GroupBy(r => (r.ResourceTypeId, r.Resource.ResourceId)).Select(r => r.First());
            IEnumerable<BulkImportResourceTypeV1Row> inputResources = resources.Select(r => r.BulkImportResource);

            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
            {
                try
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
                catch (SqlException sqlEx)
                {
                    _logger.LogError(sqlEx, "Failed to merge resources. " + sqlEx.Message);

                    throw;
                }
            }
        }

        public async Task CleanBatchResourceAsync(string resourceType, long beginSequenceId, long endSequenceId, CancellationToken cancellationToken)
        {
            short resourceTypeId = _model.GetResourceTypeId(resourceType);

            await BatchDeleteResourcesInternalAsync(beginSequenceId, endSequenceId, resourceTypeId, _importTaskConfiguration.SqlCleanResourceBatchSize, cancellationToken);
            await BatchDeleteResourceWriteClaimsInternalAsync(beginSequenceId, endSequenceId, _importTaskConfiguration.SqlCleanResourceBatchSize, cancellationToken);

            foreach (var tableName in SearchParameterTables.ToArray())
            {
                await BatchDeleteResourceParamsInternalAsync(tableName, beginSequenceId, endSequenceId, resourceTypeId, _importTaskConfiguration.SqlCleanResourceBatchSize, cancellationToken);
            }
        }

        public async Task PreprocessAsync(CancellationToken cancellationToken)
        {
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
                    using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
                    {
                        try
                        {
                            VLatest.DisableIndex.PopulateCommand(sqlCommandWrapper, index.tableName, index.indexName);
                            await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
                        }
                        catch (SqlException sqlEx)
                        {
                            _logger.LogInformation(sqlEx, "Failed to disable indexes.");

                            throw;
                        }
                    }
                }
            }
        }

        public async Task PostprocessAsync(CancellationToken cancellationToken)
        {
            // Not rerebuild index by default
            if (_importTaskConfiguration.DisableOptionalIndexesForImport || _importTaskConfiguration.DisableUniqueOptionalIndexesForImport)
            {
                List<(string tableName, string indexName)> indexesNeedRebuild = new List<(string tableName, string indexName)>();

                if (_importTaskConfiguration.DisableOptionalIndexesForImport)
                {
                    indexesNeedRebuild.AddRange(OptionalIndexesForImport.Select(indexRecord => (indexRecord.table.TableName, indexRecord.index.IndexName)));
                }

                if (_importTaskConfiguration.DisableUniqueOptionalIndexesForImport)
                {
                    indexesNeedRebuild.AddRange(OptionalUniqueIndexesForImport.Select(indexRecord => (indexRecord.table.TableName, indexRecord.index.IndexName)));
                }

                List<Task<(string tableName, string indexName)>> runningTasks = new List<Task<(string tableName, string indexName)>>();
                HashSet<string> runningRebuildTables = new HashSet<string>();

                // rebuild index operation on same table would be blocked, try to parallel run rebuild operation on different table.
                while (indexesNeedRebuild.Count > 0)
                {
                    // if all remine indexes' table has some running rebuild operation, need to wait until at least one operation completed.
                    while (indexesNeedRebuild.All(ix => runningRebuildTables.Contains(ix.tableName)) || runningTasks.Count >= _importTaskConfiguration.SqlMaxRebuildIndexOperationConcurrentCount)
                    {
                        Task<(string tableName, string indexName)> completedTask = await Task.WhenAny(runningTasks.ToArray());
                        (string tableName, string indexName) indexRebuilt = await completedTask;

                        runningRebuildTables.Remove(indexRebuilt.tableName);
                        runningTasks.Remove(completedTask);
                    }

                    (string tableName, string indexName) nextIndex = indexesNeedRebuild.Where(ix => !runningRebuildTables.Contains(ix.tableName)).First();
                    indexesNeedRebuild.Remove(nextIndex);
                    runningRebuildTables.Add(nextIndex.tableName);
                    runningTasks.Add(ExecuteRebuildIndexTaskAsync(nextIndex.tableName, nextIndex.indexName, cancellationToken));
                }

                await Task.WhenAll(runningTasks.ToArray());
            }
        }

        private async Task<(string tableName, string indexName)> ExecuteRebuildIndexTaskAsync(string tableName, string indexName, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
            {
                try
                {
                    sqlCommandWrapper.CommandTimeout = _importTaskConfiguration.SqlLongRunningOperationTimeoutInSec;

                    VLatest.RebuildIndex.PopulateCommand(sqlCommandWrapper, tableName, indexName);
                    await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);

                    return (tableName, indexName);
                }
                catch (SqlException sqlEx)
                {
                    _logger.LogInformation(sqlEx, "Failed to rebuild indexes.");

                    throw;
                }
            }
        }

        private async Task BatchDeleteResourcesInternalAsync(long beginSequenceId, long endSequenceId, short resourceTypeId, int batchSize, CancellationToken cancellationToken)
        {
            while (true)
            {
                using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
                using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
                {
                    try
                    {
                        sqlCommandWrapper.CommandTimeout = _importTaskConfiguration.SqlBulkOperationTimeoutInSec;

                        VLatest.BatchDeleteResources.PopulateCommand(sqlCommandWrapper, resourceTypeId, beginSequenceId, endSequenceId, batchSize);
                        int impactRows = await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);

                        if (impactRows < batchSize)
                        {
                            return;
                        }
                    }
                    catch (SqlException sqlEx)
                    {
                        _logger.LogInformation(sqlEx, "Failed batch delete Resource.");

                        throw;
                    }
                }
            }
        }

        private async Task BatchDeleteResourceWriteClaimsInternalAsync(long beginSequenceId, long endSequenceId, int batchSize, CancellationToken cancellationToken)
        {
            while (true)
            {
                using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
                using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
                {
                    try
                    {
                        sqlCommandWrapper.CommandTimeout = _importTaskConfiguration.SqlBulkOperationTimeoutInSec;

                        VLatest.BatchDeleteResourceWriteClaims.PopulateCommand(sqlCommandWrapper, beginSequenceId, endSequenceId, batchSize);
                        int impactRows = await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);

                        if (impactRows < batchSize)
                        {
                            return;
                        }
                    }
                    catch (SqlException sqlEx)
                    {
                        _logger.LogInformation(sqlEx, "Failed batch delete ResourceWriteClaims.");

                        throw;
                    }
                }
            }
        }

        private async Task BatchDeleteResourceParamsInternalAsync(string tableName, long beginSequenceId, long endSequenceId, short resourceTypeId, int batchSize, CancellationToken cancellationToken)
        {
            while (true)
            {
                using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
                using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
                {
                    try
                    {
                        sqlCommandWrapper.CommandTimeout = _importTaskConfiguration.SqlBulkOperationTimeoutInSec;

                        VLatest.BatchDeleteResourceParams.PopulateCommand(sqlCommandWrapper, tableName, resourceTypeId, beginSequenceId, endSequenceId, batchSize);
                        int impactRows = await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);

                        if (impactRows < batchSize)
                        {
                            return;
                        }
                    }
                    catch (SqlException sqlEx)
                    {
                        _logger.LogInformation(sqlEx, "Failed batch delete ResourceParams.");

                        throw;
                    }
                }
            }
        }
    }
}

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
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema.Model;
using Index = Microsoft.Health.SqlServer.Features.Schema.Model.Index;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public class SqlServerFhirDataBulkImportOperation : IFhirDataBulkImportOperation
    {
        private const int LongRunningCommandTimeoutInSec = 60 * 30;
        private const int BulkOperationRunningCommandTimeoutInSec = 60 * 10;
        private const int MaxDeleteDuplicateOperationCount = 8;

        private SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private ISqlServerTransientFaultRetryPolicyFactory _sqlServerTransientFaultRetryPolicyFactory;
        private SqlServerFhirModel _model;
        private ILogger<SqlServerFhirDataBulkImportOperation> _logger;

        public SqlServerFhirDataBulkImportOperation(
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            ISqlServerTransientFaultRetryPolicyFactory sqlServerTransientFaultRetryPolicyFactory,
            SqlServerFhirModel model,
            ILogger<SqlServerFhirDataBulkImportOperation> logger)
        {
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(sqlServerTransientFaultRetryPolicyFactory, nameof(sqlServerTransientFaultRetryPolicyFactory));
            EnsureArg.IsNotNull(model, nameof(model));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _sqlServerTransientFaultRetryPolicyFactory = sqlServerTransientFaultRetryPolicyFactory;
            _model = model;
            _logger = logger;
        }

        public static IReadOnlyList<(Table table, Index index)> UnclusteredIndexes { get; } =
            new List<(Table table, Index index)>()
            {
                (VLatest.Resource, VLatest.Resource.IX_Resource_ResourceTypeId_ResourceId),
                (VLatest.Resource, VLatest.Resource.IX_Resource_ResourceTypeId_ResourceId_Version),
            };

        public static IReadOnlyList<string> SearchParameterTables { get; } =
            new List<string>()
            {
                VLatest.ResourceWriteClaim.TableName,
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

                try
                {
                    await _sqlServerTransientFaultRetryPolicyFactory.Create().ExecuteAsync(
                        async () =>
                        {
                            bulkCopy.BulkCopyTimeout = BulkOperationRunningCommandTimeoutInSec;
                            await bulkCopy.WriteToServerAsync(dataTable.CreateDataReader());
                        });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to bulk copy data.");

                    throw;
                }
            }
        }

        public async Task CleanBatchResourceAsync(string resourceType, long beginSequenceId, long endSequenceId, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
            {
                try
                {
                    sqlCommandWrapper.CommandTimeout = BulkOperationRunningCommandTimeoutInSec;

                    short resourceTypeId = _model.GetResourceTypeId(resourceType);

                    VLatest.DeleteBatchResources.PopulateCommand(sqlCommandWrapper, resourceTypeId, beginSequenceId, endSequenceId);
                    await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (SqlException sqlEx)
                {
                    _logger.LogError(sqlEx, $"Failed to remove context.");

                    throw;
                }
            }
        }

        public async Task PreprocessAsync(CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
            {
                try
                {
                    IndexTableTypeV1Row[] indexes = UnclusteredIndexes.Select(indexRecord => new IndexTableTypeV1Row(indexRecord.table.TableName, indexRecord.index.IndexName)).ToArray();

                    VLatest.DisableIndexes.PopulateCommand(sqlCommandWrapper, indexes);
                    await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (SqlException sqlEx)
                {
                    _logger.LogError(sqlEx, $"Failed to disable indexes.");

                    throw;
                }
            }
        }

        public async Task PostprocessAsync(int concurrentCount, CancellationToken cancellationToken)
        {
            IndexTableTypeV1Row[] allIndexes = UnclusteredIndexes.Select(indexRecord => new IndexTableTypeV1Row(indexRecord.table.TableName, indexRecord.index.IndexName)).ToArray();
            List<Task> runningTasks = new List<Task>();

            foreach (IndexTableTypeV1Row index in allIndexes)
            {
                while (runningTasks.Count >= concurrentCount)
                {
                    Task completedTask = await Task.WhenAny(runningTasks.ToArray());
                    await completedTask;

                    runningTasks.Remove(completedTask);
                }

                runningTasks.Add(ExecuteRebuildIndexTaskAsync(index, cancellationToken));
            }

            await Task.WhenAll(runningTasks.ToArray());
        }

        public async Task DeleteDuplicatedResourcesAsync(CancellationToken cancellationToken)
        {
            try
            {
                await ExecuteDeleteDuplicatedResourcesTaskAsync(cancellationToken);

                List<Task> runningTasks = new List<Task>();

                foreach (var tableName in SearchParameterTables.ToArray())
                {
                    if (runningTasks.Count >= MaxDeleteDuplicateOperationCount)
                    {
                        Task completedTask = await Task.WhenAny(runningTasks);
                        runningTasks.Remove(completedTask);
                        await completedTask;
                    }

                    runningTasks.Add(ExecuteDeleteDuplicatedSearchParamsTaskAsync(tableName, cancellationToken));
                }

                while (runningTasks.Count > 0)
                {
                    await runningTasks.First();
                    runningTasks.RemoveAt(0);
                }
            }
            catch (SqlException sqlEx)
            {
                _logger.LogError(sqlEx, $"Failed to delete duplicate resources.");

                throw;
            }
        }

        private async Task ExecuteRebuildIndexTaskAsync(IndexTableTypeV1Row index, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
            {
                try
                {
                    sqlCommandWrapper.CommandTimeout = LongRunningCommandTimeoutInSec;

                    VLatest.RebuildIndexes.PopulateCommand(sqlCommandWrapper, new IndexTableTypeV1Row[] { index });
                    await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (SqlException sqlEx)
                {
                    _logger.LogError(sqlEx, $"Failed to rebuild indexes.");

                    throw;
                }
            }
        }

        private async Task ExecuteDeleteDuplicatedResourcesTaskAsync(CancellationToken cancellationToken)
        {
            {
                using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
                using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
                {
                    try
                    {
                        sqlCommandWrapper.CommandTimeout = LongRunningCommandTimeoutInSec;

                        VLatest.DeleteDuplicatedResources.PopulateCommand(sqlCommandWrapper);
                        await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
                    }
                    catch (SqlException sqlEx)
                    {
                        _logger.LogError(sqlEx, $"Failed to delete resoueces duplicate search paramters.");

                        throw;
                    }
                }
            }
        }

        private async Task ExecuteDeleteDuplicatedSearchParamsTaskAsync(string tableName, CancellationToken cancellationToken)
        {
            {
                using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
                using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
                {
                    try
                    {
                        sqlCommandWrapper.CommandTimeout = LongRunningCommandTimeoutInSec;

                        VLatest.DeleteDuplicatedSearchParams.PopulateCommand(sqlCommandWrapper, tableName);
                        await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
                    }
                    catch (SqlException sqlEx)
                    {
                        _logger.LogError(sqlEx, $"Failed to delete duplicate search paramters.");

                        throw;
                    }
                }
            }
        }
    }
}

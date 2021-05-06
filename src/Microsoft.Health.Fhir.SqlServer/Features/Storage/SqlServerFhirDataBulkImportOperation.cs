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
using Index = Microsoft.Health.SqlServer.Features.Schema.Model.Index;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public class SqlServerFhirDataBulkImportOperation : IFhirDataBulkImportOperation
    {
        private const int LongRunningCommandTimeoutInSec = 60 * 10;

        private SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private ISqlServerTransientFaultRetryPolicyFactory _sqlServerTransientFaultRetryPolicyFactory;
        private ILogger<SqlServerFhirDataBulkImportOperation> _logger;

        public SqlServerFhirDataBulkImportOperation(
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            ISqlServerTransientFaultRetryPolicyFactory sqlServerTransientFaultRetryPolicyFactory,
            ILogger<SqlServerFhirDataBulkImportOperation> logger)
        {
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(sqlServerTransientFaultRetryPolicyFactory, nameof(sqlServerTransientFaultRetryPolicyFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _sqlServerTransientFaultRetryPolicyFactory = sqlServerTransientFaultRetryPolicyFactory;
            _logger = logger;
        }

        public IReadOnlyList<(string tableName, Index index)> UnclusteredIndexes { get; } =
            new List<(string tableName, Index index)>()
            {
                (VLatest.Resource.TableName, VLatest.Resource.IX_Resource_ResourceSurrogateId),
                (VLatest.Resource.TableName, VLatest.Resource.IX_Resource_ResourceTypeId_ResourceId),
                (VLatest.Resource.TableName, VLatest.Resource.IX_Resource_ResourceTypeId_ResourceId_Version),
                (VLatest.Resource.TableName, VLatest.Resource.IX_Resource_ResourceTypeId_ResourceSurrgateId),
                (VLatest.CompartmentAssignment.TableName, VLatest.CompartmentAssignment.IX_CompartmentAssignment_CompartmentTypeId_ReferenceResourceId),
                (VLatest.DateTimeSearchParam.TableName, VLatest.DateTimeSearchParam.IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime),
                (VLatest.DateTimeSearchParam.TableName, VLatest.DateTimeSearchParam.IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime_Long),
                (VLatest.DateTimeSearchParam.TableName, VLatest.DateTimeSearchParam.IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime),
                (VLatest.DateTimeSearchParam.TableName, VLatest.DateTimeSearchParam.IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime_Long),
                (VLatest.NumberSearchParam.TableName, VLatest.NumberSearchParam.IX_NumberSearchParam_SearchParamId_HighValue_LowValue),
                (VLatest.NumberSearchParam.TableName, VLatest.NumberSearchParam.IX_NumberSearchParam_SearchParamId_LowValue_HighValue),
                (VLatest.NumberSearchParam.TableName, VLatest.NumberSearchParam.IX_NumberSearchParam_SearchParamId_SingleValue),
                (VLatest.QuantitySearchParam.TableName, VLatest.QuantitySearchParam.IX_QuantitySearchParam_SearchParamId_QuantityCodeId_HighValue_LowValue),
                (VLatest.QuantitySearchParam.TableName, VLatest.QuantitySearchParam.IX_QuantitySearchParam_SearchParamId_QuantityCodeId_LowValue_HighValue),
                (VLatest.QuantitySearchParam.TableName, VLatest.QuantitySearchParam.IX_QuantitySearchParam_SearchParamId_QuantityCodeId_SingleValue),
                (VLatest.ReferenceSearchParam.TableName, VLatest.ReferenceSearchParam.IX_ReferenceSearchParam_SearchParamId_ReferenceResourceTypeId_ReferenceResourceId_BaseUri_ReferenceResourceVersion),
                (VLatest.ReferenceTokenCompositeSearchParam.TableName, VLatest.ReferenceTokenCompositeSearchParam.IX_ReferenceTokenCompositeSearchParam_ReferenceResourceId1_Code2),
                (VLatest.StringSearchParam.TableName, VLatest.StringSearchParam.IX_StringSearchParam_SearchParamId_Text),
                (VLatest.StringSearchParam.TableName, VLatest.StringSearchParam.IX_StringSearchParam_SearchParamId_TextWithOverflow),
                (VLatest.TokenDateTimeCompositeSearchParam.TableName, VLatest.TokenDateTimeCompositeSearchParam.IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2),
                (VLatest.TokenDateTimeCompositeSearchParam.TableName, VLatest.TokenDateTimeCompositeSearchParam.IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2_Long),
                (VLatest.TokenDateTimeCompositeSearchParam.TableName, VLatest.TokenDateTimeCompositeSearchParam.IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2),
                (VLatest.TokenDateTimeCompositeSearchParam.TableName, VLatest.TokenDateTimeCompositeSearchParam.IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2_Long),
                (VLatest.TokenNumberNumberCompositeSearchParam.TableName, VLatest.TokenNumberNumberCompositeSearchParam.IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_LowValue2_HighValue2_LowValue3_HighValue3),
                (VLatest.TokenNumberNumberCompositeSearchParam.TableName, VLatest.TokenNumberNumberCompositeSearchParam.IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_Text2),
                (VLatest.TokenQuantityCompositeSearchParam.TableName, VLatest.TokenQuantityCompositeSearchParam.IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_HighValue2_LowValue2),
                (VLatest.TokenQuantityCompositeSearchParam.TableName, VLatest.TokenQuantityCompositeSearchParam.IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_LowValue2_HighValue2),
                (VLatest.TokenQuantityCompositeSearchParam.TableName, VLatest.TokenQuantityCompositeSearchParam.IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_SingleValue2),
                (VLatest.TokenSearchParam.TableName, VLatest.TokenSearchParam.IX_TokenSeachParam_SearchParamId_Code_SystemId),
                (VLatest.TokenStringCompositeSearchParam.TableName, VLatest.TokenStringCompositeSearchParam.IX_TokenStringCompositeSearchParam_SearchParamId_Code1_Text2),
                (VLatest.TokenStringCompositeSearchParam.TableName, VLatest.TokenStringCompositeSearchParam.IX_TokenStringCompositeSearchParam_SearchParamId_Code1_Text2WithOverflow),
                (VLatest.TokenText.TableName, VLatest.TokenText.IX_TokenText_SearchParamId_Text),
                (VLatest.TokenTokenCompositeSearchParam.TableName, VLatest.TokenTokenCompositeSearchParam.IX_TokenTokenCompositeSearchParam_Code1_Code2),
                (VLatest.UriSearchParam.TableName, VLatest.UriSearchParam.IX_UriSearchParam_SearchParamId_Uri),

                // ResourceWriteClaim Table - No unclustered index
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
                            await bulkCopy.WriteToServerAsync(dataTable.CreateDataReader());
                        });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to bulk copy data.");

                    throw;
                }
            }
        }

        public async Task CleanBatchResourceAsync(long startSurrogateId, long endSurrogateId, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
            {
                try
                {
                    VLatest.DeleteBatchResources.PopulateCommand(sqlCommandWrapper, startSurrogateId, endSurrogateId);
                    await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (SqlException sqlEx)
                {
                    _logger.LogError(sqlEx, $"Failed to remove context.");

                    throw;
                }
            }
        }

        public async Task DisableIndexesAsync(CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
            {
                try
                {
                    IndexTableTypeV1Row[] indexes = UnclusteredIndexes.Select(indexRecord => new IndexTableTypeV1Row(indexRecord.tableName, indexRecord.index.IndexName)).ToArray();

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

        public async Task RebuildIndexesAsync(CancellationToken cancellationToken)
        {
            IndexTableTypeV1Row[] allIndexes = UnclusteredIndexes.Select(indexRecord => new IndexTableTypeV1Row(indexRecord.tableName, indexRecord.index.IndexName)).ToArray();
            List<IndexTableTypeV1Row[]> indexBatches = new List<IndexTableTypeV1Row[]>();
            int batchCount = 3;
            for (int i = 0; i < allIndexes.Length; i += batchCount)
            {
                indexBatches.Add(allIndexes.Skip(i).Take(batchCount).ToArray());
            }

            foreach (IndexTableTypeV1Row[] indexes in indexBatches)
            {
                using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
                using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
                {
                    try
                    {
                        sqlCommandWrapper.CommandTimeout = LongRunningCommandTimeoutInSec;

                        VLatest.RebuildIndexes.PopulateCommand(sqlCommandWrapper, indexes);
                        await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
                    }
                    catch (SqlException sqlEx)
                    {
                        _logger.LogError(sqlEx, $"Failed to rebuild indexes.");

                        throw;
                    }
                }
            }
        }

        public async Task DeleteDuplicatedResourcesAsync(CancellationToken cancellationToken)
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
                    _logger.LogError(sqlEx, $"Failed to remove duplicated resources.");

                    throw;
                }
            }
        }
    }
}

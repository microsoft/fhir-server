// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Client;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public class SqlServerFhirDataBulkOperation : IFhirDataBulkOperation
    {
        private SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private ISqlServerTransientFaultRetryPolicyFactory _sqlServerTransientFaultRetryPolicyFactory;
        private ILogger<SqlServerFhirDataBulkOperation> _logger;

        public SqlServerFhirDataBulkOperation(
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            ISqlServerTransientFaultRetryPolicyFactory sqlServerTransientFaultRetryPolicyFactory,
            ILogger<SqlServerFhirDataBulkOperation> logger)
        {
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(sqlServerTransientFaultRetryPolicyFactory, nameof(sqlServerTransientFaultRetryPolicyFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _sqlServerTransientFaultRetryPolicyFactory = sqlServerTransientFaultRetryPolicyFactory;
            _logger = logger;
        }

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
                    VLatest.HardDeleteBatchResource.PopulateCommand(sqlCommandWrapper, startSurrogateId, endSurrogateId);
                    await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (SqlException sqlEx)
                {
                    _logger.LogError(sqlEx, $"Failed to remove context.");

                    throw;
                }
            }
        }
    }
}

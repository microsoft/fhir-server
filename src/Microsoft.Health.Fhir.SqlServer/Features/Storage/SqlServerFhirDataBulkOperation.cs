// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
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
        private ILogger<SqlServerFhirDataBulkOperation> _logger;

        public SqlServerFhirDataBulkOperation(
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            ILogger<SqlServerFhirDataBulkOperation> logger)
        {
            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _logger = logger;
        }

        public async Task CleanResourceAsync(long startSurrogateId, long endSurrogateId, CancellationToken cancellationToken)
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

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.TaskManagement;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public class SqlServerTaskContextUpdater : IContextUpdater
    {
        private string _taskId;
        private string _runId;

        private SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private ILogger<SqlServerTaskContextUpdater> _logger;

        public SqlServerTaskContextUpdater(
            string taskId,
            string runId,
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            ILogger<SqlServerTaskContextUpdater> logger)
        {
            EnsureArg.IsNotNullOrEmpty(taskId, nameof(taskId));
            EnsureArg.IsNotNullOrEmpty(runId, nameof(runId));
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _taskId = taskId;
            _runId = runId;
            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _logger = logger;
        }

        public async Task UpdateContextAsync(string context, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
            {
                try
                {
                    VLatest.UpdateTaskContext.PopulateCommand(sqlCommandWrapper, _taskId, context, _runId);
                    await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (SqlException sqlEx)
                {
                    _logger.LogInformation(sqlEx, "Failed to update context.");

                    if (sqlEx.Number == SqlErrorCodes.NotFound)
                    {
                        throw new TaskNotExistException(sqlEx.Message);
                    }

                    throw;
                }
            }
        }
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.TaskManagement;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public class SqlServerTaskManager : ITaskManager
    {
        private SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private SchemaInformation _schemaInformation;
        private ILogger<SqlServerTaskManager> _logger;

        public SqlServerTaskManager(
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            SchemaInformation schemaInformation,
            ILogger<SqlServerTaskManager> logger)
        {
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _schemaInformation = schemaInformation;
            _logger = logger;
        }

        public async Task<TaskInfo> CreateTaskAsync(TaskInfo task, bool isUniqueTaskByType, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                try
                {
                    if (_schemaInformation.Current >= SchemaVersionConstants.SupportParentTask)
                    {
                        VLatest.CreateTask.PopulateCommand(sqlCommandWrapper, task.TaskId, task.QueueId, task.TaskTypeId, task.ParentTaskId ?? string.Empty, task.MaxRetryCount, task.InputData, isUniqueTaskByType);
                    }
                    else
                    {
                        V30.CreateTask.PopulateCommand(sqlCommandWrapper, task.TaskId, task.QueueId, task.TaskTypeId, task.MaxRetryCount, task.InputData, isUniqueTaskByType);
                    }

                    SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);

                    return sqlDataReader.ReadTaskInfo();
                }
                catch (SqlException sqlEx)
                {
                    if (sqlEx.Number == SqlErrorCodes.Conflict)
                    {
                        throw new TaskConflictException(sqlEx.Message);
                    }

                    throw;
                }
            }
        }

        public async Task<TaskInfo> GetTaskAsync(string taskId, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                VLatest.GetTaskDetails.PopulateCommand(sqlCommandWrapper, taskId);
                SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);

                return sqlDataReader.ReadTaskInfo();
            }
        }

        public async Task<TaskInfo> CancelTaskAsync(string taskId, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                try
                {
                    VLatest.CancelTask.PopulateCommand(sqlCommandWrapper, taskId);
                    SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);

                    return sqlDataReader.ReadTaskInfo();
                }
                catch (SqlException sqlEx)
                {
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

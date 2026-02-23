// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.GetJobStatus;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.JobManagement;
using Microsoft.Health.SqlServer.Features.Client;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations
{
    /// <summary>
    /// SQL Server implementation of the job status service.
    /// </summary>
    public class SqlJobStatusService : IJobStatusService
    {
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly IUrlResolver _urlResolver;
        private readonly ILogger<SqlJobStatusService> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlJobStatusService"/> class.
        /// </summary>
        /// <param name="sqlConnectionWrapperFactory">The SQL connection wrapper factory.</param>
        /// <param name="urlResolver">The URL resolver.</param>
        /// <param name="logger">The logger.</param>
        public SqlJobStatusService(
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            IUrlResolver urlResolver,
            ILogger<SqlJobStatusService> logger)
        {
            _sqlConnectionWrapperFactory = EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            _urlResolver = EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<JobStatusInfo>> GetAllJobStatusAsync(CancellationToken cancellationToken)
        {
            var jobs = new List<JobStatusInfo>();

            var queueTypes = new[]
            {
                QueueType.Export,
                QueueType.Import,
                QueueType.Reindex,
                QueueType.BulkDelete,
                QueueType.BulkUpdate,
            };

            using SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true);
            using SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand();

            foreach (var queueType in queueTypes)
            {
                var queueJobs = await GetJobsByQueueTypeAsync(sqlCommandWrapper, queueType, cancellationToken);
                jobs.AddRange(queueJobs);
            }

            return jobs;
        }

        private async Task<List<JobStatusInfo>> GetJobsByQueueTypeAsync(SqlCommandWrapper sqlCommandWrapper, QueueType queueType, CancellationToken cancellationToken)
        {
            var jobs = new List<JobStatusInfo>();
            var operationName = GetOperationNameForQueueType(queueType);

            sqlCommandWrapper.CommandText = @"
                SELECT GroupId, min(Status) AS Status, min(CreateDate) AS CreateDate, max(EndDate) AS EndDate
                FROM dbo.JobQueue
                WHERE QueueType = @QueueType
                  AND Status <> 5
                GROUP BY GroupId
                ORDER BY CreateDate DESC";

            sqlCommandWrapper.Parameters.Clear();
            sqlCommandWrapper.Parameters.AddWithValue("@QueueType", (byte)queueType);

            try
            {
                using SqlDataReader reader = await sqlCommandWrapper.ExecuteReaderAsync(CommandBehavior.Default, cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var groupId = reader.GetInt64(0);
                    var status = (JobStatus)reader.GetByte(1);
                    var createDate = reader.GetDateTime(2);
                    DateTime? endDate = await reader.IsDBNullAsync(5, cancellationToken) ? null : reader.GetDateTime(3);

                    var jobStatusInfo = new JobStatusInfo
                    {
                        GroupId = groupId,
                        QueueType = queueType,
                        JobType = operationName,
                        Status = status,
                        ContentLocation = _urlResolver.ResolveOperationResultUrl(operationName, groupId.ToString()),
                        CreateDate = createDate,
                        EndDate = endDate,
                    };

                    jobs.Add(jobStatusInfo);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error retrieving jobs for queue type {QueueType}", queueType);
            }

            return jobs;
        }

        private static string GetOperationNameForQueueType(QueueType queueType)
        {
            return queueType switch
            {
                QueueType.Export => OperationsConstants.Export,
                QueueType.Import => OperationsConstants.Import,
                QueueType.Reindex => OperationsConstants.Reindex,
                QueueType.BulkDelete => OperationsConstants.BulkDelete,
                QueueType.BulkUpdate => OperationsConstants.BulkUpdate,
                _ => queueType.ToString(),
            };
        }
    }
}
